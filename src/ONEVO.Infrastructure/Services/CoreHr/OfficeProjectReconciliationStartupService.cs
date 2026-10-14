using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Services.CoreHr;

/// <summary>Runs <see cref="IOfficeProjectReconciler"/> once for every live tenant shortly after
/// startup: the backfill for existing tenants and the recovery path for anything missed. Not a
/// periodic job (SCL-01: background jobs have no multi-instance locking). Skipped in the Test
/// environment so integration-test hosts don't create Office projects behind the tests' backs.</summary>
public sealed class OfficeProjectReconciliationStartupService(
    IServiceProvider services,
    IHostEnvironment environment,
    ILogger<OfficeProjectReconciliationStartupService> logger,
    TimeSpan? startupDelay = null,
    TimeSpan? retryDelay = null) : BackgroundService
{
    public const int PageSize = 200;
    public const int MaxAttempts = 3;
    private static readonly TimeSpan DefaultStartupDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Test"))
            return;

        try
        {
            await Task.Delay(startupDelay ?? DefaultStartupDelay, stoppingToken);
            await RunWithRetriesAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down before or during the run.
        }
        catch (Exception ex)
        {
            // BackgroundServiceExceptionBehavior = StopHost: a one-off backfill must never take
            // the API down. Anything missed is retried on the next start or the next time the
            // Office project is needed.
            logger.LogError(ex, "Office project reconciliation startup run failed.");
        }
    }

    /// <summary>Bounded recovery: a failed pass (e.g. the tenant list can't be read) is rerun, and
    /// tenants whose reconcile threw are retried, up to <see cref="MaxAttempts"/> attempts in all.
    /// Whatever still fails is left to the next trigger or restart.</summary>
    private async Task RunWithRetriesAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid>? pending = null; // null: every live tenant (a full pass)
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                pending = pending is null ? await RunOnceAsync(ct) : await ReconcileTenantsAsync(pending, ct);
                if (pending.Count == 0)
                    return;
                logger.LogWarning(
                    "Office project reconciliation failed for {Count} tenant(s) (attempt {Attempt} of {MaxAttempts}).",
                    pending.Count, attempt, MaxAttempts);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Office project reconciliation startup pass failed (attempt {Attempt} of {MaxAttempts}).",
                    attempt, MaxAttempts);
            }

            if (attempt < MaxAttempts)
                await Task.Delay(retryDelay ?? DefaultRetryDelay, ct);
        }

        logger.LogError(
            "Office project reconciliation gave up after {MaxAttempts} attempts; remaining tenants are retried on the next trigger or restart.",
            MaxAttempts);
    }

    /// <summary>One full pass over every live tenant. Returns the tenants whose reconcile threw.</summary>
    public async Task<IReadOnlyList<Guid>> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var reconciler = scope.ServiceProvider.GetRequiredService<IOfficeProjectReconciler>();

        var failed = new List<Guid>();
        var skip = 0;
        while (true)
        {
            var page = await tenants.ListAsync(statusFilter: null, searchTerm: null, skip, PageSize, ct);
            foreach (var tenant in page)
            {
                if (tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled)
                    continue;
                if (!await TryReconcileAsync(reconciler, tenant.Id, ct))
                    failed.Add(tenant.Id);
            }

            if (page.Count < PageSize)
                break;
            skip += PageSize;
        }

        return failed;
    }

    private async Task<IReadOnlyList<Guid>> ReconcileTenantsAsync(IReadOnlyList<Guid> tenantIds, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var reconciler = scope.ServiceProvider.GetRequiredService<IOfficeProjectReconciler>();
        var failed = new List<Guid>();
        foreach (var tenantId in tenantIds)
        {
            if (!await TryReconcileAsync(reconciler, tenantId, ct))
                failed.Add(tenantId);
        }
        return failed;
    }

    private async Task<bool> TryReconcileAsync(IOfficeProjectReconciler reconciler, Guid tenantId, CancellationToken ct)
    {
        try
        {
            var outcome = await reconciler.ReconcileAsync(tenantId, ct);
            logger.LogDebug("Office project reconciliation for tenant {TenantId}: {Outcome}.", tenantId, outcome);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Office project reconciliation failed for tenant {TenantId}.", tenantId);
            return false;
        }
    }
}
