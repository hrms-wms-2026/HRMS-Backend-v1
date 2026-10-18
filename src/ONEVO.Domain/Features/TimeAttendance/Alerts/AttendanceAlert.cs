using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Alerts;

/// <summary>The attendance signals an alert can be about. The keys match the employee signal catalogue.</summary>
public static class AttendanceAlertSignals
{
    public const string AbsentDays = "absent_days";
    public const string LateClockIns = "late_clock_ins";
    public const string EarlyClockOuts = "early_clock_outs";
    public const string ShortHoursDays = "short_hours_days";
    public const string OffScheduleWork = "off_schedule_work";
    public const string LocationViolations = "location_violations";
    public const string IdleActivityAlerts = "idle_activity_alerts";
    public const string MissingClockOuts = "missing_clock_outs";

    /// <summary>Signals backed by a notification row (SourceId = notification id) rather than by a day.</summary>
    public static readonly IReadOnlySet<string> NotificationBacked = new HashSet<string> { LocationViolations, IdleActivityAlerts };
}

public static class AttendanceAlertStatuses
{
    public const string Open = "open";
    public const string Resolved = "resolved";
}

/// <summary>How an attendance alert was resolved. Excused and converted-to-leave stop it counting; confirmed keeps it.</summary>
public static class AttendanceAlertOutcomes
{
    public const string Excused = "excused";
    public const string Confirmed = "confirmed";
    public const string ConvertedToLeave = "converted_to_leave";

    public static bool StopsCounting(string? outcome) => outcome is Excused or ConvertedToLeave;
}

/// <summary>
/// One attendance problem of one employee on one finished day (or one location/idle notification), materialised so a
/// reviewer can resolve it in the Inbox (plan E1). Resolving it as excused or converted-to-leave takes the day out of
/// every attendance count; confirmed keeps it counted but marks it reviewed.
/// </summary>
public class AttendanceAlert : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public Guid? LegalEntityId { get; set; }
    public DateOnly WorkDate { get; set; }
    public string SignalKey { get; set; } = string.Empty;
    /// <summary>The notification behind a location/idle alert; null for day-based signals.</summary>
    public Guid? SourceId { get; set; }
    public string Severity { get; set; } = "warning";
    public string Status { get; set; } = AttendanceAlertStatuses.Open;
    public string? Outcome { get; set; }
    public string? Note { get; set; }
    public Guid? ResolvedByEmployeeId { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ConvertedLeaveRequestId { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public string? DetailsJson { get; set; }
}
