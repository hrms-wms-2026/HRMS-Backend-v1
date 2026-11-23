using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ONEVO.Application.Common.Observability;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Observability;

/// <summary>Outbox state at one moment. <see cref="OldestPendingAge"/> is null when nothing is waiting.</summary>
public sealed record OutboxBacklog(int Pending, int Failed, TimeSpan? OldestPendingAge, DateTimeOffset MeasuredAt);

public interface IOutboxBacklogSource
{
    Task<OutboxBacklog> GetAsync(CancellationToken ct);
}

/// <summary>
/// Task 17: counts the outbox backlog with one small query, cached for <see cref="CacheFor"/> so the readiness check
/// and the metrics never add load. The three gauges (crownus.outbox.pending, .failed, .oldest_pending_age) read the
/// last snapshot and ask for a fresh one in the background when it is stale - a gauge callback must not block.
/// </summary>
public sealed class OutboxBacklogProbe : IOutboxBacklogSource
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    private OutboxBacklog? _latest;
    private static OutboxBacklogProbe? _current;
    private static readonly object GaugeLock = new();
    private static bool _gaugesRegistered;

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _refresh = new(1, 1);

    public OutboxBacklogProbe(IServiceScopeFactory scopes, TimeProvider? time = null)
    {
        _scopes = scopes;
        _time = time ?? TimeProvider.System;
        _current = this;
        RegisterGauges();
    }

    public async Task<OutboxBacklog> GetAsync(CancellationToken ct)
    {
        var cached = _latest;
        if (cached is not null && _time.GetUtcNow() - cached.MeasuredAt < CacheFor)
            return cached;

        await _refresh.WaitAsync(ct);
        try
        {
            cached = _latest;
            if (cached is not null && _time.GetUtcNow() - cached.MeasuredAt < CacheFor)
                return cached;
            return _latest = await MeasureAsync(ct);
        }
        finally
        {
            _refresh.Release();
        }
    }

    private async Task<OutboxBacklog> MeasureAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        // The outbox is platform-shared: System mode sees every tenant's rows (as OutboxProcessor does).
        scope.ServiceProvider.GetRequiredService<IWritableTenantContext>().SetSystemMode();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var counts = await db.Set<OutboxMessage>().AsNoTracking()
            .Where(m => m.Status == OutboxMessageStatus.Pending || m.Status == OutboxMessageStatus.Failed)
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Oldest = g.Min(m => m.CreatedAt) })
            .ToListAsync(ct);

        var now = _time.GetUtcNow();
        var pending = counts.FirstOrDefault(c => c.Status == OutboxMessageStatus.Pending);
        var failed = counts.FirstOrDefault(c => c.Status == OutboxMessageStatus.Failed);
        return new OutboxBacklog(
            pending?.Count ?? 0,
            failed?.Count ?? 0,
            pending is null ? null : now - pending.Oldest,
            now);
    }

    private static void RegisterGauges()
    {
        lock (GaugeLock)
        {
            if (_gaugesRegistered)
                return;
            _gaugesRegistered = true;
        }

        CrownusMetrics.Meter.CreateObservableGauge("crownus.outbox.pending",
            () => Observe(b => b.Pending), unit: "{message}", description: "Outbox messages waiting to be dispatched.");
        CrownusMetrics.Meter.CreateObservableGauge("crownus.outbox.failed",
            () => Observe(b => b.Failed), unit: "{message}", description: "Outbox messages that gave up after their retries.");
        CrownusMetrics.Meter.CreateObservableGauge("crownus.outbox.oldest_pending_age",
            () => Observe(b => (int)(b.OldestPendingAge?.TotalSeconds ?? 0)), unit: "s",
            description: "Age of the oldest waiting outbox message.");
    }

    private static IEnumerable<Measurement<int>> Observe(Func<OutboxBacklog, int> value)
    {
        var probe = _current;
        if (probe is null)
            return [];
        var latest = probe._latest;
        if (latest is null || probe._time.GetUtcNow() - latest.MeasuredAt >= CacheFor)
            _ = probe.RefreshInBackgroundAsync();
        return latest is null ? [] : [new Measurement<int>(value(latest))];
    }

    private async Task RefreshInBackgroundAsync()
    {
        try
        {
            await GetAsync(CancellationToken.None);
        }
        catch
        {
            // A failed measurement leaves the last value in place; the readiness check reports the failure.
        }
    }
}

/// <summary>
/// Readiness entry for the outbox. Degraded (still HTTP 200), never Unhealthy: a stuck outbox is the same for
/// every instance, so failing readiness would take the whole site out of the load balancer for a problem that only
/// delays notifications. Alert on the degraded status or on crownus.outbox.oldest_pending_age instead.
/// </summary>
public sealed class OutboxBacklogHealthCheck(IOutboxBacklogSource probe) : IHealthCheck
{
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(15);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        OutboxBacklog backlog;
        try
        {
            backlog = await probe.GetAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Degraded("Outbox backlog could not be read.", ex);
        }

        var data = new Dictionary<string, object>
        {
            ["pending"] = backlog.Pending,
            ["failed"] = backlog.Failed,
            ["oldestPendingSeconds"] = (int)(backlog.OldestPendingAge?.TotalSeconds ?? 0),
        };
        return backlog.OldestPendingAge > StuckAfter
            ? HealthCheckResult.Degraded($"Oldest outbox message has waited more than {StuckAfter.TotalMinutes:0} minutes.", data: data)
            : HealthCheckResult.Healthy("Outbox is moving.", data);
    }
}
