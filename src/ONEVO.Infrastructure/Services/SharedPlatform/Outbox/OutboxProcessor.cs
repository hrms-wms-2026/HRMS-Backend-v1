using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Outbox;

/// <summary>
/// Polls the outbox and dispatches pending messages to their registered
/// IOutboxMessageHandler. Failures are retried with exponential backoff; after
/// MaxAttempts the message is parked as failed. Also purges expired idempotency
/// records once per hour.
/// </summary>
public sealed class OutboxProcessor : BackgroundService
{
    private const int BatchSize = 20;
    private const int MaxAttempts = 8;
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);

    private readonly IServiceProvider _services;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly TimeSpan _pollInterval;
    private DateTimeOffset _lastPurge = DateTimeOffset.MinValue;

    public OutboxProcessor(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<OutboxProcessor> logger)
    {
        _services = services;
        _logger = logger;
        var pollSeconds = configuration.GetValue("Outbox:PollSeconds", 10);
        _pollInterval = TimeSpan.FromSeconds(Math.Max(1, pollSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
                await PurgeIdempotencyRecordsIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The loop must survive transient failures (e.g. DB briefly down).
                _logger.LogWarning(ex, "Outbox processing iteration failed; will retry.");
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var encryption = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var tenantSwitcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();
        var writableTenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
        var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var handlers = scope.ServiceProvider.GetServices<IOutboxMessageHandler>()
            .ToDictionary(h => h.Type, StringComparer.Ordinal);

        var now = clock.UtcNow;
        var due = await db.Set<OutboxMessage>()
            .Where(m => m.Status == OutboxMessageStatus.Pending && m.NextAttemptAt <= now)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0)
            return;

        foreach (var message in due)
        {
            // This scope's DbContext/connection starts in system mode (no RLS tenant set), which
            // is correct for reading the platform-scoped outbox table above but wrong for any
            // tenant-scoped row a handler writes (e.g. Notification) - RLS rejects it outright.
            // Switch to the message's own tenant before invoking its handler, same mechanism
            // request-time middleware and LeaveYearEndEntitlementJob use.
            if (message.TenantId is { } tenantId)
            {
                var tenant = await tenantRepository.GetByIdAsync(tenantId, ct);
                if (tenant is not null)
                    await tenantSwitcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);
            }
            else
            {
                writableTenantContext.SetSystemMode();
            }

            try
            {
                if (!handlers.TryGetValue(message.Type, out var handler))
                    throw new InvalidOperationException($"No outbox handler registered for type '{message.Type}'.");

                var payloadJson = encryption.Decrypt(message.EncryptedPayload);
                await handler.HandleAsync(payloadJson, ct);

                message.Status = OutboxMessageStatus.Published;
                message.ProcessedAt = clock.UtcNow;
                message.LastError = null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                message.AttemptCount++;
                message.LastError = Truncate(ex.Message, 2000);

                if (message.AttemptCount >= MaxAttempts)
                {
                    message.Status = OutboxMessageStatus.Failed;
                    message.ProcessedAt = clock.UtcNow;
                    _logger.LogError(ex,
                        "Outbox message {MessageId} ({Type}) failed permanently after {Attempts} attempts.",
                        message.Id, message.Type, message.AttemptCount);
                }
                else
                {
                    var delaySeconds = BaseRetryDelay.TotalSeconds * Math.Pow(2, message.AttemptCount - 1);
                    var delay = TimeSpan.FromSeconds(Math.Min(delaySeconds, MaxRetryDelay.TotalSeconds));
                    message.NextAttemptAt = clock.UtcNow.Add(delay);
                    _logger.LogWarning(ex,
                        "Outbox message {MessageId} ({Type}) attempt {Attempt} failed; next attempt at {NextAttempt}.",
                        message.Id, message.Type, message.AttemptCount, message.NextAttemptAt);
                }
            }

            // Saved per-message, immediately after its own tenant context is active on the
            // connection - a single batched SaveChangesAsync at the end would flush every
            // message's changes under whichever tenant was switched to last.
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task PurgeIdempotencyRecordsIfDueAsync(CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow - _lastPurge < PurgeInterval)
            return;
        _lastPurge = DateTimeOffset.UtcNow;

        await using var scope = _services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        var purged = await store.PurgeExpiredAsync(ct);
        if (purged > 0)
            _logger.LogInformation("Purged {Count} expired idempotency records.", purged);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
