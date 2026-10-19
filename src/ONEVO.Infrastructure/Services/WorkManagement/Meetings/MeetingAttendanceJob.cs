using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Services.WorkManagement.Meetings;

/// <summary>
/// Every 15 minutes (spec 5.8): when Meetings:Background is on and this instance holds the <c>meetings.attendance</c> lease,
/// syncs each tenant's due series links (<see cref="MeetingAttendanceSyncService"/>) under that tenant's own context, then
/// cancels parked links nothing needs any more (spec 5.7). One link failing never stops the rest; three failures in a row
/// for the same link raise an error-level alert. A lost lease ends the run.
/// </summary>
public sealed class MeetingAttendanceJob(IServiceProvider services, IOptions<MeetingsOptions> options, IDateTimeProvider clock,
    ILogger<MeetingAttendanceJob> logger) : BackgroundService
{
    public const string JobName = "meetings.attendance";
    public static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);
    private const int AlertAfterFailures = 3;

    private readonly ConcurrentDictionary<Guid, int> _failuresInARow = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await RunOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "MeetingAttendanceJob run failed."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        if (!options.Value.Background)
            return;
        var leases = services.GetRequiredService<IJobLeaseService>();
        if (!await leases.TryAcquireAsync(JobName, LeaseTtl, ct))
            return;

        try
        {
            await using var scope = services.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var tenants = provider.GetRequiredService<ITenantRepository>();
            var switcher = provider.GetRequiredService<ITenantContextSwitcher>();
            // Meeting tables are under FORCE row-level security: tenant list in admin mode, each tenant's rows under its own context.
            provider.GetRequiredService<IWritableTenantContext>().SetAdminMode();

            foreach (var tenant in await tenants.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, ct))
            {
                ct.ThrowIfCancellationRequested();
                if (!await leases.RenewAsync(JobName, LeaseTtl, ct))
                    return;
                try
                {
                    await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);
                    await RunForTenantAsync(provider, tenant.Id, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    provider.GetRequiredService<IUnitOfWork>().ClearTracking();
                    logger.LogWarning(ex, "Meeting attendance sync failed for tenant {TenantId}; skipping.", tenant.Id);
                }
            }
        }
        finally
        {
            await leases.ReleaseAsync(JobName, CancellationToken.None);
        }
    }

    private async Task RunForTenantAsync(IServiceProvider provider, Guid tenantId, CancellationToken ct)
    {
        var sync = provider.GetRequiredService<MeetingAttendanceSyncService>();
        var unitOfWork = provider.GetRequiredService<IUnitOfWork>();
        var now = clock.UtcNow;
        foreach (var linkId in await sync.ListDueLinksAsync(tenantId, now, ct))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var outcome = await sync.SyncLinkAsync(tenantId, linkId, now, ct);
                if (outcome == MeetingAttendanceSyncOutcome.ProviderError)
                    CountFailure(tenantId, linkId, null);
                else if (outcome != MeetingAttendanceSyncOutcome.Busy)
                    _failuresInARow.TryRemove(linkId, out _);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                unitOfWork.ClearTracking();
                CountFailure(tenantId, linkId, ex);
            }
        }

        var links = provider.GetRequiredService<IMeetingLinkRepository>();
        var linkService = provider.GetRequiredService<SeriesMeetingLinkService>();
        foreach (var linkId in await links.ListParkedLinkIdsAsync(tenantId, ct))
        {
            try
            {
                await linkService.CancelIfUnneededAsync(tenantId, linkId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                unitOfWork.ClearTracking();
                logger.LogWarning(ex, "Releasing parked meeting link {LinkId} (tenant {TenantId}) failed; retried next run.", linkId, tenantId);
            }
        }
    }

    private void CountFailure(Guid tenantId, Guid linkId, Exception? ex)
    {
        var failures = _failuresInARow.AddOrUpdate(linkId, 1, (_, n) => n + 1);
        if (failures >= AlertAfterFailures)
            logger.LogError(ex, "Meeting attendance sync for link {LinkId} (tenant {TenantId}) failed {Failures} times in a row.",
                linkId, tenantId, failures);
        else
            logger.LogWarning(ex, "Meeting attendance sync for link {LinkId} (tenant {TenantId}) failed; retried at its next checkpoint.",
                linkId, tenantId);
    }
}
