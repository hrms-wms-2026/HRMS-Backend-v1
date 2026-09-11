using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Services.Calendar;

/// <summary>
/// Runs CalendarSyncService.SyncConnectionAsync for every non-disabled external calendar
/// connection, across every active tenant, every 15 minutes. Same admin-mode tenant enumeration +
/// per-tenant SwitchToTenantAsync shape as LeaveYearEndEntitlementJob/BulkOnboardingBatchProcessor
/// - IWritableTenantContext.SetAdminMode() is required before ITenantRepository.ListAsync so the
/// tenant listing itself isn't scoped to whatever tenant context this DI scope happened to start
/// in (a fresh scope defaults to system mode, but admin mode is this codebase's established
/// convention for "enumerate all tenants" jobs).
/// </summary>
public sealed class CalendarSyncJob(IServiceProvider services, ILogger<CalendarSyncJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private const int TenantPageSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "CalendarSyncJob run failed."); }
        }
    }

    /// <summary>Public entry for tests / manual triggers - same precedent as
    /// LeaveYearEndEntitlementJob.RunForYearAsync.</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
        tenantContext.SetAdminMode();

        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var switcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();

        var skip = 0;
        while (true)
        {
            var page = await tenants.ListAsync(TenantStatus.Active, searchTerm: null, skip, TenantPageSize, ct);
            if (page.Count == 0) break;

            foreach (var tenant in page)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

                    var connections = scope.ServiceProvider.GetRequiredService<IExternalCalendarConnectionRepository>();
                    var syncService = scope.ServiceProvider.GetRequiredService<ICalendarSyncService>();

                    foreach (var connection in await connections.GetActiveAsync(ct))
                    {
                        if (connection.SyncDirection == CalendarSyncDirections.Disabled) continue;

                        try
                        {
                            await syncService.SyncConnectionAsync(tenant.Id, connection.Id, ct);
                        }
                        catch (Exception ex)
                        {
                            // SyncConnectionAsync already catches Pull/Push errors internally, but a
                            // handful of things outside that internal catch can still throw here (the
                            // initial connection lookup, the final Update+SaveChangesAsync, the
                            // token-refresh-failure branch's own SaveChangesAsync - e.g. a
                            // DbUpdateException from a concurrent manual "sync now" request on the
                            // same connection). Isolate per connection so one bad connection doesn't
                            // skip the rest of this tenant's connections.
                            logger.LogWarning(ex, "Calendar sync failed for connection {ConnectionId} (tenant {TenantId}); skipping.", connection.Id, tenant.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Isolate per tenant, mirroring LeaveYearEndEntitlementJob.RunForYearAsync's
                    // per-tenant try/catch - one tenant's database/context issue (e.g.
                    // SwitchToTenantAsync or GetActiveAsync throwing) must not abort every other
                    // tenant's sync in the same run.
                    logger.LogWarning(ex, "Calendar sync failed for tenant {TenantId}; skipping.", tenant.Id);
                }
            }

            skip += TenantPageSize;
        }
    }
}
