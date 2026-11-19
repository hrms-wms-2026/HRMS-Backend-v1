namespace ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

/// <summary>Status is one of present | late | missing_clock_out | leave | off | absent | none.
/// "none" means outside employment (before hire / after termination), in the future, or today
/// not yet clocked in.</summary>
public sealed record EmployeeAttendanceDay(DateOnly Date, string Status);

/// <summary>WorkingDays/Absent come from the expected-workday calendar (legal entity week minus
/// holidays, clipped to hire/termination/today), not from which attendance rows exist.</summary>
public sealed record EmployeeAttendanceOverviewResponse(
    DateOnly From,
    DateOnly To,
    int WorkingDays,
    int Present,
    int Late,
    int MissingClockOuts,
    int LeaveDays,
    IReadOnlyList<EmployeeAttendanceDay> Days,
    int Absent,
    int ShortHours,
    int WorkedOnNonWorkingDay,
    int WorkedDuringTimeOff);

/// <summary>LocationViolations is null (and LocationTrackingEnabled false) when work-location
/// verification is not enabled for this employee. It counts OutsideWorkLocationAlert
/// notifications, i.e. alert events (6-hour cooldown), not GPS samples.</summary>
public sealed record EmployeeAttendanceDisciplineResponse(
    DateOnly From,
    DateOnly To,
    int LateClockIns,
    int EarlyClockOuts,
    int MissingClockOuts,
    int OverBreakDays,
    int OverBreakMinutes,
    bool LocationTrackingEnabled,
    int? LocationViolations,
    EmployeeAttendanceDisciplineMetrics? Previous = null);

public sealed record EmployeeAttendanceDisciplineMetrics(
    int LateClockIns,
    int EarlyClockOuts,
    int MissingClockOuts,
    int OverBreakDays,
    int OverBreakMinutes,
    int? LocationViolations);

/// <summary>
/// One day on the Attendance card's day list. Status uses the card's rules (present | late |
/// missing_clock_out | leave | absent | off). Clock times are given both as instants and as "HH:mm" in
/// the employee's attendance timezone; minutes are whole minutes (0 when not applicable).
/// </summary>
public sealed record EmployeeAttendanceDayDetail(
    DateOnly Date,
    string Status,
    bool ExpectedWorkingDay,
    string? HolidayName,
    string? ScheduledStart,
    string? ScheduledEnd,
    DateTimeOffset? ClockInAt,
    DateTimeOffset? ClockOutAt,
    string? ClockInLocal,
    string? ClockOutLocal,
    int WorkedMinutes,
    int? RequiredWorkMinutes,
    int BreakMinutes,
    int LateMinutes,
    int EarlyLeaveMinutes,
    bool ShortHours,
    int OverBreakMinutes,
    string? WorkMode);

/// <summary>Every day of from..to that has already happened (or today), newest first. Timezone: the
/// employee's attendance timezone id, so a day opened in the detail drawer shows the same clock times.</summary>
public sealed record EmployeeAttendanceDaysResponse(
    DateOnly From, DateOnly To, string Timezone, IReadOnlyList<EmployeeAttendanceDayDetail> Days);
