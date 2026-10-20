using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Domain.Features.TimeAttendance.Alerts;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Alerts;

public sealed record AttendanceAlertMaterializeResult(int Added, int Removed);

public interface IAttendanceAlertMaterializer
{
    Task<AttendanceAlertMaterializeResult> MaterializeAsync(Guid tenantId, Guid employeeId, Guid? legalEntityId, CancellationToken ct);
}

/// <summary>
/// Turns one employee's finished days (the last <see cref="WindowDays"/> days up to yesterday) into attendance alerts a
/// reviewer can resolve (plan E1). Day signals come from <see cref="AttendancePeriodCalculator"/> over the raw data (the
/// alert carries its own resolution, so excused days still materialise); location and idle alerts come from the
/// monitoring notifications, one alert per notification. Open rows are upserted; an open, never-resolved row whose day
/// no longer qualifies (a later correction or leave) is removed; resolved rows are always kept.
/// </summary>
public sealed class AttendanceAlertMaterializer : IAttendanceAlertMaterializer
{
    public const int WindowDays = 35;
    private const int MaxNotifications = 500;

    private static readonly IReadOnlyCollection<NotificationType> LocationTypes = [NotificationType.OutsideWorkLocationAlert];
    // The same notification types the idle signal counts (GetEmployeeOverviewSignalsQueryHandler).
    private static readonly IReadOnlyCollection<NotificationType> IdleTypes = [NotificationType.LongIdleAlert, NotificationType.LowActivityAlert];

    private readonly IEmployeeAttendancePeriodReader _reader;
    private readonly IAttendanceAlertRepository _alerts;
    private readonly INotificationRepository _notifications;
    private readonly IDateTimeProvider _clock;
    private readonly IEmployeeClockInPolicyResolver? _policies;
    private readonly AttendanceDeductionService? _deductions;
    private readonly IAttendanceAlertDeductionRepository? _deductionRows;

    public AttendanceAlertMaterializer(
        IEmployeeAttendancePeriodReader reader, IAttendanceAlertRepository alerts, INotificationRepository notifications, IDateTimeProvider clock,
        IEmployeeClockInPolicyResolver? policies = null, AttendanceDeductionService? deductions = null,
        IAttendanceAlertDeductionRepository? deductionRows = null)
    {
        _reader = reader;
        _alerts = alerts;
        _notifications = notifications;
        _clock = clock;
        _policies = policies;
        _deductions = deductions;
        _deductionRows = deductionRows;
    }

    public async Task<AttendanceAlertMaterializeResult> MaterializeAsync(
        Guid tenantId, Guid employeeId, Guid? legalEntityId, CancellationToken ct)
    {
        var from = _clock.Today.AddDays(-WindowDays);
        var to = _clock.Today.AddDays(-1);
        var data = await _reader.LoadAsync(tenantId, employeeId, legalEntityId, new EmployeePeriod(from, to), ct);
        var dates = AttendancePeriodCalculator.Classify(data with { Excused = null }).Dates;

        var qualifying = new Dictionary<(DateOnly Date, string Signal, Guid? Source), string?>();
        void Days(IEnumerable<DateOnly> days, string signal)
        {
            foreach (var day in days.Where(d => d >= from && d <= to))
                qualifying[(day, signal, null)] = null;
        }
        Days(dates.Absent, AttendanceAlertSignals.AbsentDays);
        Days(dates.Late, AttendanceAlertSignals.LateClockIns);
        Days(dates.EarlyDepartures, AttendanceAlertSignals.EarlyClockOuts);
        Days(dates.WorkedOnNonWorkingDay, AttendanceAlertSignals.OffScheduleWork);
        Days(dates.MissingClockOuts, AttendanceAlertSignals.MissingClockOuts);

        foreach (var (types, signal) in new[] { (LocationTypes, AttendanceAlertSignals.LocationViolations), (IdleTypes, AttendanceAlertSignals.IdleActivityAlerts) })
        {
            var notifications = await _notifications.ListByTypesAsync(
                tenantId, employeeId, types, data.RangeStartUtc, data.RangeEndUtc, MaxNotifications, ct);
            foreach (var notification in notifications)
            {
                var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(notification.CreatedAt, data.Timezone).DateTime);
                if (day >= from && day <= to)
                    qualifying[(day, signal, notification.Id)] = notification.Title;
            }
        }

        var existing = await _alerts.ListForEmployeeAsync(tenantId, employeeId, from, to, ct);
        var removed = 0;
        var removedAlerts = new HashSet<Guid>();
        foreach (var alert in existing)
        {
            if (alert.Status == AttendanceAlertStatuses.Open && alert.Outcome is null
                && !qualifying.ContainsKey((alert.WorkDate, alert.SignalKey, alert.SourceId)))
            {
                _alerts.Remove(alert);
                removedAlerts.Add(alert.Id);
                removed++;
            }
        }

        var known = existing.Select(a => (a.WorkDate, a.SignalKey, a.SourceId)).ToHashSet();
        var added = 0;
        var addedAlerts = new List<AttendanceAlert>();
        var now = _clock.UtcNow;
        foreach (var ((day, signal, source), title) in qualifying)
        {
            if (known.Contains((day, signal, source)))
                continue;
            var newAlert = new AttendanceAlert
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employeeId,
                LegalEntityId = legalEntityId,
                WorkDate = day,
                SignalKey = signal,
                SourceId = source,
                Status = AttendanceAlertStatuses.Open,
                DetectedAt = now,
                DetailsJson = title is null ? null : System.Text.Json.JsonSerializer.Serialize(new { title }),
            };
            _alerts.Add(newAlert);
            addedAlerts.Add(newAlert);
            added++;
        }

        var reconciled = await ReconcileDeductionsAsync(
            tenantId, employeeId, legalEntityId, data, existing.Concat(addedAlerts).ToList(), removedAlerts, ct);

        if (added > 0 || removed > 0 || reconciled)
            await _alerts.SaveChangesAsync(ct);
        return new AttendanceAlertMaterializeResult(added, removed);
    }

    /// <summary>
    /// Brings every late/early alert's leave deduction in line with what its day costs now under the employee's
    /// clock-in policy: the policy row the missing minutes reach, unless the alert was excused (or removed because the
    /// day no longer qualifies). Resolved Confirmed alerts are included, so a later correction or leave still refunds.
    /// Days before the policy's rules last changed are never newly charged. True when anything was reconciled.
    /// </summary>
    private async Task<bool> ReconcileDeductionsAsync(
        Guid tenantId, Guid employeeId, Guid? legalEntityId, AttendancePeriodData data, IReadOnlyList<AttendanceAlert> alerts,
        IReadOnlySet<Guid> removedAlerts, CancellationToken ct)
    {
        if (_policies is null || _deductions is null || _deductionRows is null || legalEntityId is not Guid entityId)
            return false;
        var lateEarly = alerts
            .Where(a => a.SignalKey is AttendanceAlertSignals.LateClockIns or AttendanceAlertSignals.EarlyClockOuts)
            .ToList();
        if (lateEarly.Count == 0)
            return false;

        var charged = await _deductionRows.ListByAlertsAsync(tenantId, lateEarly.Select(a => a.Id).ToList(), ct);
        var records = data.Records.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.First());
        var policies = new Dictionary<DateOnly, ClockInPolicy?>();
        var any = false;
        foreach (var alert in lateEarly)
        {
            DeductionTarget? target = null;
            if (!removedAlerts.Contains(alert.Id) && !AttendanceAlertOutcomes.StopsCounting(alert.Outcome)
                && records.TryGetValue(alert.WorkDate, out var record))
            {
                var (late, early) = AttendancePeriodCalculator.MissingMinutes(record, data);
                var minutes = alert.SignalKey == AttendanceAlertSignals.LateClockIns ? late : early;
                if (!policies.TryGetValue(alert.WorkDate, out var policy))
                    policies[alert.WorkDate] = policy = await _policies.ResolveAsync(tenantId, employeeId, entityId, alert.WorkDate, ct);
                var rule = ClockInPolicyRules.Match(policy, minutes);
                if (rule is not null && (charged.ContainsKey(alert.Id) || Chargeable(policy, alert.WorkDate)))
                    target = new DeductionTarget(minutes, rule);
            }
            if (target is null && !charged.ContainsKey(alert.Id))
                continue;
            await _deductions.ReconcileAsync(tenantId, alert, target, null, ct);
            any = true;
        }
        return any;
    }

    /// <summary>No back-charging: only days on or after the rules last changed; a policy never stamped charges nothing.</summary>
    private static bool Chargeable(ClockInPolicy? policy, DateOnly workDate) =>
        policy?.DeductionRulesChangedAt is DateTimeOffset changedAt && workDate >= DateOnly.FromDateTime(changedAt.UtcDateTime);
}
