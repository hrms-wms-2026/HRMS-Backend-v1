namespace ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

/// <summary>Status is one of present | late | missing_clock_out | leave | off | absent | none.</summary>
public sealed record EmployeeAttendanceDay(DateOnly Date, string Status);

public sealed record EmployeeAttendanceOverviewResponse(
    DateOnly From,
    DateOnly To,
    int WorkingDays,
    int Present,
    int Late,
    int MissingClockOuts,
    int LeaveDays,
    IReadOnlyList<EmployeeAttendanceDay> Days);

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
