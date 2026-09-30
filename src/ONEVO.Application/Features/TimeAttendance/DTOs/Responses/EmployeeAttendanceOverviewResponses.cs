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
