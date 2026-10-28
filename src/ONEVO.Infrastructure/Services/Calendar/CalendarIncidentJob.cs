using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Exceptions;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Services.Calendar;

/// <summary>
/// The nightly calendar meeting-exception pass (plan C2, calendar doc §6.7): per active tenant, under its own context,
/// every employee invited to a meeting in the evaluation window is re-evaluated (the window rules - decline rate,
/// overload, off-hours - and every threshold roll-up land here), then unattached incidents older than
/// <see cref="CalendarExceptionDefaults.IncidentRetentionDays"/> days are purged. One employee or tenant failing never
/// stops the rest.
/// </summary>
public sealed class CalendarIncidentJob(IServiceProvider services, ILogger<CalendarIncidentJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try { await RunOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "CalendarIncidentJob run failed."); }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    /// <summary>Returns the number of cases opened.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var tenants = provider.GetRequiredService<ITenantRepository>();
        var switcher = provider.GetRequiredService<ITenantContextSwitcher>();
        var tenantContext = provider.GetRequiredService<IWritableTenantContext>();
        var events = provider.GetRequiredService<ICalendarEventRepository>();
        var meetings = provider.GetRequiredService<IMeetingRepository>();
        var engine = provider.GetRequiredService<CalendarIncidentEngine>();
        var incidents = provider.GetRequiredService<ICalendarIncidentRepository>();
        var unitOfWork = provider.GetRequiredService<IUnitOfWork>();
        var clock = provider.GetRequiredService<IDateTimeProvider>();

        tenantContext.SetAdminMode();
        var now = clock.UtcNow;
        var opened = 0;
        foreach (var tenant in await tenants.ListAsync(TenantStatus.Active, null, 0, int.MaxValue, ct))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);
                var windowFrom = now.AddDays(-CalendarExceptionDefaults.LongWindowDays);
                var windowTo = now.AddDays(CalendarIncidentEngine.LookAheadDays);
                // Calendar-event participants, plus task-meeting attendees: a series participant has no calendar event
                // row, so the first query never finds someone whose only meetings are task meetings.
                var employeeIds = (await events.ListParticipantEmployeeIdsInRangeAsync(tenant.Id, windowFrom, windowTo, ct))
                    .Concat(await meetings.ListAttendeeEmployeeIdsInRangeAsync(tenant.Id, windowFrom, windowTo, ct))
                    .Distinct().ToList();
                foreach (var employeeId in employeeIds)
                {
                    try
                    {
                        opened += (await engine.EvaluateEmployeeAsync(tenant.Id, employeeId, ct)).CasesOpened.Count;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        unitOfWork.ClearTracking();
                        logger.LogWarning(ex, "Calendar incident evaluation failed for employee {EmployeeId} in tenant {TenantId}.", employeeId, tenant.Id);
                    }
                }
                if (await incidents.PurgeUnattachedAsync(tenant.Id, now.AddDays(-CalendarExceptionDefaults.IncidentRetentionDays), ct) > 0)
                    await incidents.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                unitOfWork.ClearTracking();
                logger.LogWarning(ex, "Calendar incident pass failed for tenant {TenantId}; skipping.", tenant.Id);
            }
        }
        return opened;
    }
}
