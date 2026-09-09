using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Helpers;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
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
        var workLocations = scope.ServiceProvider.GetRequiredService<IEmployeeWorkLocationRepository>();
        var clockInPolicies = scope.ServiceProvider.GetRequiredService<IClockInPolicyRepository>();
        var expectedWorkAreas = scope.ServiceProvider.GetRequiredService<IExpectedWorkAreaResolver>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var now = clock.UtcNow;
        var keys = await deviceState.GetActiveEmployeeKeysAsync(now - LookbackWindow, ct);
        var created = 0;

        foreach (var (tenantId, employeeId) in keys)
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

            var workArea = confirmation?.LocationType switch
            {
                DailyWorkLocationConfirmation.LocationTypeOffice => "onsite",
                DailyWorkLocationConfirmation.LocationTypeHome or DailyWorkLocationConfirmation.LocationTypeOther => "remote",
                _ => null
            };

            if (workArea is null)
            {
                var resolved = await expectedWorkAreas.ResolveAsync(employee, legalEntity, workDate, ct);
                workArea = resolved.IsSuccess ? resolved.Value!.WorkArea : null;
            }

            if (workArea is not ("onsite" or "remote")) continue;

            var policies = await clockInPolicies.ListByLegalEntityAsync(tenantId, legalEntityId, includeInactive: false, ct);
            var active = ClockInPolicyResolver.ResolveActiveFullCompanyPolicies(policies, workDate);
            if (active.Count != 1 || active[0].AllowedRadiusMeters is not { } radiusMeters) continue;

            (double Latitude, double Longitude)? referencePoint = workArea switch
            {
                "onsite" when legalEntity is { OfficeLatitude: double officeLat, OfficeLongitude: double officeLon }
                    => (officeLat, officeLon),
                "remote" when confirmation is { Latitude: double confLat, Longitude: double confLon }
                    => (confLat, confLon),
                "remote" => await ResolveRegisteredWorkLocationAsync(tenantId, employeeId, workLocations, ct),
                _ => null
            };
            if (referencePoint is null) continue;

            var distance = GeoDistanceCalculator.DistanceMeters(
                located.Latitude!.Value, located.Longitude!.Value,
                referencePoint.Value.Latitude, referencePoint.Value.Longitude);
            if (distance <= radiusMeters) continue;

            await notifications.AddAsync(new Notification
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employeeId,
                Type = NotificationType.OutsideWorkLocationAlert,
                Title = "Outside approved work location",
                Message = $"A location sample was {distance:F0}m from your approved {workArea} location.",
                MetadataJson = $$"""{"distanceMeters":{{distance:F0}},"workArea":"{{workArea}}"}""",
                CreatedAt = now
            }, ct);
            created++;
        }

        if (created > 0)
            await notifications.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Location rule evaluation finished. EmployeesScanned={Count} NotificationsCreated={Created}",
            keys.Count, created);
    }

    private static async Task<(double, double)?> ResolveRegisteredWorkLocationAsync(
        Guid tenantId, Guid employeeId, IEmployeeWorkLocationRepository workLocations, CancellationToken ct)
    {
        var registered = await workLocations.GetByEmployeeIdAsync(tenantId, employeeId, ct);
        return registered is null ? null : (registered.Latitude, registered.Longitude);
    }
}
