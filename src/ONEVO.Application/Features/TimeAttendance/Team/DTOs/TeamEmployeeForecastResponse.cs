using ONEVO.Application.Features.WorkManagement.Monitoring.Services;

namespace ONEVO.Application.Features.TimeAttendance.Team.DTOs;

/// <summary>GET work/forecast/employees (spec 2026-11-08 §4b). Hours rounded to 2 decimals, rates to 4 (0.38 = 38%).
/// Totals cover every covered employee (not only the page, not narrowed by status or search).</summary>
public sealed record TeamEmployeeForecastResponse(
    DateOnly AsOf, DateOnly WindowStart, DateOnly WindowEnd,
    TeamEmployeeForecastTotals Totals,
    IReadOnlyList<RateTrendPoint> RateTrend,
    IReadOnlyList<TeamEmployeeForecastItem> Items,
    int Total, int Page, int PageSize);

public sealed record TeamEmployeeForecastTotals(
    int People, int OnTrack, int RaiseRate, int Overtime, int NoHistory,
    decimal RemainingHours, decimal CapacityHours, decimal PredictedHours, decimal OvertimeHours,
    decimal? TeamRate, decimal? RequiredRate);

/// <param name="Deadline">The worst window's deadline; null when the employee has no open estimated task due today or later.</param>
public sealed record TeamEmployeeForecastItem(
    Guid EmployeeId, string Name, Guid? AvatarFileId, string? Department,
    int WorkedMinutes, int TaskMinutes, decimal? ConversionRate,
    DateOnly? Deadline, decimal RemainingHours, decimal CapacityHours, decimal PredictedHours,
    decimal? RequiredRate, decimal OvertimeHours, string Status,
    IReadOnlyList<TeamEmployeeForecastModule> Modules);

/// <param name="RemainingHours">The employee's share of the remaining hours of their open, estimated tasks in the Module.</param>
/// <param name="OpenTasks">Their open assigned tasks in the Module (any due date).</param>
public sealed record TeamEmployeeForecastModule(
    Guid ModuleId, Guid ProjectId, string ProjectName, string Title, DateOnly EndDate, decimal RemainingHours, int OpenTasks);
