using ONEVO.Application.Features.WorkManagement.Monitoring.Services;

namespace ONEVO.Application.Features.WorkManagement.Forecast.DTOs;

/// <summary>GET work/forecast/modules (spec 2026-11-08 §4a). Hours are rounded to 2 decimals, rates to 4 (0.42 = 42%).
/// Totals and project bars count only top-most listed modules (a parent's allocation already holds its children's);
/// the status counts and <see cref="Total"/> count every listed module.</summary>
public sealed record ModuleForecastResponse(
    DateOnly AsOf, DateOnly WindowStart, DateOnly WindowEnd,
    ModuleForecastTotals Totals,
    IReadOnlyList<RateTrendPoint> RateTrend,
    IReadOnlyList<ModuleForecastProject> Projects,
    IReadOnlyList<ModuleForecastItem> Items,
    int Total, int Page, int PageSize);

public sealed record ModuleForecastTotals(
    int Modules, int OnTrack, int RaiseRate, int Overload, int NoHistory,
    decimal AllocatedHours, decimal CompletedHours, decimal RemainingHours, decimal CapacityHours, decimal PredictedHours,
    decimal OvertimeHours, decimal? TeamRate, decimal? RequiredRate,
    decimal MaxTaskRate);

public sealed record ModuleForecastProject(
    Guid ProjectId, string Name, decimal AllocatedHours, decimal RemainingHours, decimal PredictedHours, decimal CapacityHours,
    decimal OvertimeHours, int ModulesAtRisk);

public sealed record ModuleForecastItem(
    Guid ModuleId, Guid ProjectId, string ProjectName, string Title, string Path,
    DateOnly StartDate, DateOnly EndDate, int WorkingDaysLeft,
    decimal AllocatedHours, decimal CompletedHours, decimal RemainingHours, decimal CapacityHours,
    decimal? ConversionRate, decimal PredictedHours, decimal? RequiredRate, decimal OvertimeHours,
    string Status, int MemberCount, IReadOnlyList<ModuleForecastMember> Members);

public sealed record ModuleForecastMember(
    Guid EmployeeId, string Name, Guid? AvatarFileId, decimal? ConversionRate, int WorkedMinutes, int TaskMinutes);

public static class ForecastRounding
{
    public static decimal Hours(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    public static decimal? Rate(decimal? value) => value is { } v ? Math.Round(v, 4, MidpointRounding.AwayFromZero) : null;
}
