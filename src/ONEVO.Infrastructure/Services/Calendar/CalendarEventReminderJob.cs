using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Services.Calendar;

/// <summary>
/// Every <see cref="CheckInterval"/>: per active tenant, under that tenant's own context, sends the start reminders that
/// are due (plan C1 P2-2, <see cref="CalendarEventReminderService"/>). One tenant failing never stops the rest.
/// </summary>
public sealed class CalendarEventReminderJob(IServiceProvider services, ILogger<CalendarEventReminderJob> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await RunOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "CalendarEventReminderJob run failed."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var tenants = provider.GetRequiredService<ITenantRepository>();
        var switcher = provider.GetRequiredService<ITenantContextSwitcher>();
        var tenantContext = provider.GetRequiredService<IWritableTenantContext>();
        var reminders = provider.GetRequiredService<CalendarEventReminderService>();
        var unitOfWork = provider.GetRequiredService<IUnitOfWork>();
        var clock = provider.GetRequiredService<IDateTimeProvider>();

        // Calendar tables are under FORCE row-level security: tenant list in admin mode, each tenant's rows under its own context.
        tenantContext.SetAdminMode();
        var now = clock.UtcNow;
        var sent = 0;
        foreach (var tenant in await tenants.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, ct))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);
                sent += await reminders.SendDueAsync(tenant.Id, now, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                unitOfWork.ClearTracking();
                logger.LogWarning(ex, "Calendar reminders failed for tenant {TenantId}; skipping.", tenant.Id);
            }
        }
        return sent;
    }
}
