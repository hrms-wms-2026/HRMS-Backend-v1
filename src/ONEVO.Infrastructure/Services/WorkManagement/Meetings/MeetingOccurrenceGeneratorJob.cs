using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Services.WorkManagement.Meetings;

/// <summary>
/// Hourly (spec 5.5): when Meetings:Background is on and this instance holds the job lease, runs
/// <see cref="MeetingOccurrenceGenerator"/> for every active tenant under that tenant's own context. One tenant failing
/// never stops the rest; a lost lease ends the run.
/// </summary>
public sealed class MeetingOccurrenceGeneratorJob(IServiceProvider services, IOptions<MeetingsOptions> options,
    ILogger<MeetingOccurrenceGeneratorJob> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await RunOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "MeetingOccurrenceGeneratorJob run failed."); }
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
        if (!await leases.TryAcquireAsync(MeetingOccurrenceGenerator.JobName, MeetingOccurrenceGenerator.LeaseTtl, ct))
            return;

        try
        {
            await using var scope = services.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var tenants = provider.GetRequiredService<ITenantRepository>();
            var switcher = provider.GetRequiredService<ITenantContextSwitcher>();
            var unitOfWork = provider.GetRequiredService<IUnitOfWork>();
            var generator = provider.GetRequiredService<MeetingOccurrenceGenerator>();
            var stamper = provider.GetRequiredService<MeetingHoursStamper>();
            // Meeting tables are under FORCE row-level security: tenant list in admin mode, each tenant's rows under its own context.
            provider.GetRequiredService<IWritableTenantContext>().SetAdminMode();

            foreach (var tenant in await tenants.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, ct))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);
                    if (!await generator.RunForTenantAsync(tenant.Id, ct))
                        return;
                    // Meeting hours (2026-10-26 Part A): ended occurrences carry their person-hours.
                    await stamper.StampTenantAsync(tenant.Id, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    unitOfWork.ClearTracking();
                    logger.LogWarning(ex, "Meeting occurrence generation failed for tenant {TenantId}; skipping.", tenant.Id);
                }
            }
        }
        finally
        {
            await leases.ReleaseAsync(MeetingOccurrenceGenerator.JobName, CancellationToken.None);
        }
    }
}
