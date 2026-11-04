namespace ONEVO.Application.Features.TimeAttendance.Team.DTOs;

/// <summary>
/// My Team "Work vs task hours": the covered team's attendance working time (clocked time less breaks) against the
/// estimated hours of the tasks they completed, for one day, ISO week or calendar month.
/// </summary>
/// <param name="Period">day | week | month.</param>
/// <param name="TrendStart">First day of <paramref name="Trend"/>: the ISO week for a day, else the range itself.</param>
/// <param name="IsProvisional">The range contains today, so its figures are still moving.</param>
public sealed record TeamWorkTaskHoursResponse(
    string Period,
    DateOnly RangeStart,
    DateOnly RangeEnd,
    DateOnly Today,
    bool IsProvisional,
    int TeamSize,
    TeamWorkTaskTotals Totals,
    TeamWorkTaskTotals Previous,
    DateOnly TrendStart,
    DateOnly TrendEnd,
    IReadOnlyList<TeamWorkTaskTrendPoint> Trend,
    IReadOnlyList<TeamWorkTaskEmployeeRow> Employees,
    DateTimeOffset CalculatedAtUtc);

/// <param name="WorkedMinutes">Attendance working time: clocked time less breaks (an open record counts up to now today).</param>
/// <param name="TaskMinutes">Estimated hours (in minutes) of the top-level tasks completed in the range.</param>
/// <param name="UnestimatedTasks">Completed tasks without an estimate: they count 0 minutes.</param>
/// <param name="Utilization">TaskMinutes / WorkedMinutes as a percent with one decimal; null with no working time.
/// It can pass 100 when completed estimates exceed the time worked.</param>
public sealed record TeamWorkTaskTotals(
    int WorkedMinutes,
    int BreakMinutes,
    int TaskMinutes,
    int CompletedTasks,
    int UnestimatedTasks,
    decimal? Utilization);

public sealed record TeamWorkTaskTrendPoint(
    DateOnly Date,
    int WorkedMinutes,
    int BreakMinutes,
    int TaskMinutes,
    int CompletedTasks,
    decimal? Utilization);

/// <param name="Rank">Competition rank by utilization, highest first; null without working time in the range.</param>
public sealed record TeamWorkTaskEmployeeRow(
    Guid EmployeeId,
    string Name,
    Guid? AvatarFileId,
    string? Department,
    int WorkedMinutes,
    int BreakMinutes,
    int TaskMinutes,
    int CompletedTasks,
    int UnestimatedTasks,
    decimal? Utilization,
    int? Rank);
