using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Helpers;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.Monitoring.Notifications;

/// <summary>Sibling of WellnessRuleEvaluatorJob for location instead of idle time: scans recent
/// location-bearing DeviceStateSnapshot rows and writes an OutsideWorkLocationAlert notification
/// when a sample falls outside the employee's resolved reference point + radius. Flag-only - never
/// blocks anything; the employee list picks the alert up via the existing outside_work_location
/// attention type (see EfEmployeeRepository.ResolveMonitoringWarningOverridesAsync).</summary>
public sealed class LocationRuleEvaluatorJob : BackgroundService
{
    private static readonly TimeSpan EvaluationInterval = TimeSpan.FromMinutes(5);
    // A GPS fix is captured roughly every 15 minutes (DeviceStateCollector) - 30 minutes of
    // lookback tolerates one missed tick without losing coverage.
    private static readonly TimeSpan LookbackWindow = TimeSpan.FromMinutes(30);
    // Not calendar-day-scoped (matches the existing BreakReminder/LongIdleAlert cooldown design,
    // not a day boundary) - re-alerts if still out of range 6 hours later rather than going silent
    // for the rest of the day after the first sample.
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromHours(6);

    private readonly IServiceProvider _services;
    private readonly ILogger<LocationRuleEvaluatorJob> _logger;

    public LocationRuleEvaluatorJob(IServiceProvider services, ILogger<LocationRuleEvaluatorJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(EvaluationInterval);
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogWarning(ex, "Location rule evaluation iteration failed; will retry next cycle."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deviceState = scope.ServiceProvider.GetRequiredService<IDeviceStateSnapshotRepository>();
        var confirmations = scope.ServiceProvider.GetRequiredService<IDailyWorkLocationConfirmationRepository>();
        var toggles = scope.ServiceProvider.GetRequiredService<IMonitoringToggleResolver>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var tenantSwitcher = scope.ServiceProvider.GetRequiredService<ITenantContextSwitcher>();

        // Every table this job reads/writes (device_state_snapshots, employees, legal_entities,
        // daily_work_location_confirmations, monitoring_notifications) is under FORCE row-level
        // security. A background scope defaults to system mode, which the tenant_isolation
        // policy admits for none of them - so the opening cross-tenant sweep needs admin mode,
        // and each tenant's rows need that tenant's context established first. Mirrors
        // LeaveYearEndEntitlementJob / ExceptionDetectionJob.
        tenantContext.SetAdminMode();

        var now = clock.UtcNow;
        var keys = await deviceState.GetActiveEmployeeKeysAsync(now - LookbackWindow, ct);
        var created = 0;

        // Grouped tenant-major so each tenant's alerts are written - and saved - while that
        // tenant's context is active on the connection, before switching to the next tenant.
        // A single batched SaveChangesAsync across tenants would flush every tenant's changes
        // under whichever tenant was switched to last, and every earlier tenant's rows would
        // fail the RLS WITH CHECK constraint.
        foreach (var tenantGroup in keys.GroupBy(k => k.TenantId))
        {
            ct.ThrowIfCancellationRequested();

            var tenantId = tenantGroup.Key;
            var tenant = await tenants.GetByIdAsync(tenantId, ct);
            if (tenant is null) continue;
            await tenantSwitcher.SwitchToTenantAsync(
                new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

            var tenantCreated = 0;

            foreach (var (_, employeeId) in tenantGroup)
            {
                ct.ThrowIfCancellationRequested();

                var recent = await deviceState.GetRecentAsync(tenantId, employeeId, now - LookbackWindow, ct);
                var located = recent.LastOrDefault(s => s.Latitude is not null && s.Longitude is not null);
                if (located is null) continue;

                if (await notifications.ExistsRecentAsync(
                        tenantId, employeeId, NotificationType.OutsideWorkLocationAlert, now - AlertCooldown, ct))
                    continue;

                var employee = await db.Employees.AsNoTracking()
                    .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == employeeId, ct);
                if (employee?.LegalEntityId is not { } legalEntityId) continue;

                var legalEntity = await db.LegalEntities.AsNoTracking()
                    .FirstOrDefaultAsync(le => le.TenantId == tenantId && le.Id == legalEntityId, ct);
                if (legalEntity is null) continue;

                var workDate = DateOnly.FromDateTime(located.CapturedAt.UtcDateTime);
                var confirmation = await confirmations.GetForDateAsync(tenantId, employeeId, workDate, ct);

                // No daily confirmation for this date -> no asserted reference point -> skip. This is
                // strictly non-false-alarming: an unconfirmed day never fires a wrong-reference alert,
                // and it removes the dependency on ExpectedWorkAreaResolver/the old work-area string
                // entirely.
                if (confirmation is null) continue;

                (double Latitude, double Longitude)? referencePoint = confirmation.LocationType switch
                {
                    DailyWorkLocationConfirmation.LocationTypeOffice
                        when legalEntity is { OfficeLatitude: double officeLat, OfficeLongitude: double officeLon }
                        => (officeLat, officeLon),
                    DailyWorkLocationConfirmation.LocationTypeHome or DailyWorkLocationConfirmation.LocationTypeOther
                        when confirmation is { Latitude: double confLat, Longitude: double confLon }
                        => (confLat, confLon),
                    _ => null
                };
                if (referencePoint is null) continue;

                // The two-arg overload resolves by Employee.UserId and, with no legal entity given,
                // only an unambiguous single active employee for that user - neither shape fits a
                // real Employee.Id from GetActiveEmployeeKeysAsync. Use the three-arg overload with
                // the employee's own UserId + legal entity (same contract AttendanceTodayStateService
                // uses), which also resolves correctly for a multi-company user.
                var radiusMeters = await toggles.GetAllowedRadiusMetersAsync(tenantId, employee.UserId, legalEntityId, ct);
                if (radiusMeters is not { } resolvedRadiusMeters) continue;

                var distance = GeoDistanceCalculator.DistanceMeters(
                    located.Latitude!.Value, located.Longitude!.Value,
                    referencePoint.Value.Latitude, referencePoint.Value.Longitude);
                if (distance <= resolvedRadiusMeters) continue;

                await notifications.AddAsync(new Notification
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EmployeeId = employeeId,
                    Type = NotificationType.OutsideWorkLocationAlert,
                    Title = "Outside approved work location",
                    Message = $"A location sample was {distance:F0}m from your approved {confirmation.LocationType} location.",
                    MetadataJson = $$"""{"distanceMeters":{{distance:F0}},"locationType":"{{confirmation.LocationType}}"}""",
                    CreatedAt = now
                }, ct);
                tenantCreated++;
            }

            if (tenantCreated > 0)
                await notifications.SaveChangesAsync(ct);

            created += tenantCreated;
        }

        _logger.LogInformation(
            "Location rule evaluation finished. EmployeesScanned={Count} NotificationsCreated={Created}",
            keys.Count, created);
    }
}
