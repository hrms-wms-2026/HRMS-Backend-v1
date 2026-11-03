namespace ONEVO.Application.Features.WorkManagement.WeeklyProgress.Services;

/// <summary>A meeting occurrence the employee is invited to.</summary>
public sealed record WeeklyMeetingInterval(DateTimeOffset Start, DateTimeOffset End, bool IsAllDay, bool Declined, bool Cancelled);

/// <summary>One attendance record (the employee's local work date).</summary>
public sealed record WeeklyAttendanceDay(DateOnly Date, DateTimeOffset? ActualStart, DateTimeOffset? ActualEnd, int WorkedMinutes, int BreakMinutes);

/// <summary>Everything the calculator needs for one employee's week. Lists may cover more than the week; the calculator
/// clips every interval to each local day. WorkStart/WorkEnd null = the legal entity has no working hours set.</summary>
public sealed record WeeklyWorkProgressInput(
    DateOnly WeekStart,
    TimeZoneInfo Zone,
    DateTimeOffset Now,
    IReadOnlySet<int> WorkingWeekdays,
    TimeOnly? WorkStart,
    TimeOnly? WorkEnd,
    IReadOnlySet<DateOnly> Holidays,
    DateOnly HireDate,
    DateOnly? TerminationDate,
    IReadOnlyDictionary<DateOnly, decimal?> LeaveHoursByDate,
    IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> PlannedBlocks,
    IReadOnlyList<WeeklyMeetingInterval> Meetings,
    IReadOnlyList<(DateTimeOffset In, DateTimeOffset? Out)> ClockSessions,
    IReadOnlyList<WeeklyAttendanceDay> Attendance);

public static class WeeklyProgressStatuses
{
    public const string Ahead = "ahead";
    public const string OnTrack = "on_track";
    public const string Behind = "behind";
    public const string NoTarget = "no_target";
}
