using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Entities;

public class AttendanceRecord : ITenantOwnedEntity
{
    public const string SourceWeb = "web";
    public const string WorkTimeTypeFixed = "fixed";
    public const string WorkAreaOnsite = "onsite";
    public const string WorkAreaRemote = "remote";
    public const string WorkAreaHybrid = "either";
    public const string WorkAreaField = "field";
    public const string StatusOnTime = "on_time";
    public const string StatusLate = "late";
    public const string StatusActive = "active";
    public const string StatusClockedOut = "clocked_out";
    public const string StatusOffDay = "off_day";
    public const string StatusNoSchedule = "no_schedule";
    public const string StatusPolicyNotConfigured = "policy_not_configured";
    public const string StatusNotClockedIn = "not_clocked_in";
    public const string StatusShortHours = "short_hours";

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public bool ExpectedWorkingDay { get; set; }
    public string? WorkTimeType { get; set; }
    public TimeOnly? ScheduledStart { get; set; }
    public TimeOnly? ScheduledEnd { get; set; }
    public int? RequiredWorkMinutes { get; set; }
    public string? ExpectedWorkArea { get; set; }
    public string? ScheduleTimezone { get; set; }
    public bool IsHoliday { get; set; }
    public string? HolidayName { get; set; }
    public DateTimeOffset? ActualStart { get; set; }
    public DateTimeOffset? ActualEnd { get; set; }
    public int WorkedMinutes { get; set; }
    public int BreakMinutes { get; set; }
    public int? LateMinutes { get; set; }
    public string? AttendanceSource { get; set; }
    public string Status { get; set; } = StatusOffDay;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class PresenceSession : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public DateTimeOffset? FirstSeenAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public int TotalPresentMinutes { get; set; }
    public int TotalBreakMinutes { get; set; }
    public string? Source { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class BreakRecord : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTimeOffset BreakStart { get; set; }
    public DateTimeOffset? BreakEnd { get; set; }
    public string? BreakType { get; set; }
    public bool AutoDetected { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
