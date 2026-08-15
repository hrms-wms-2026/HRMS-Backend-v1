using ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Dashboard.DTOs;

public enum MonitoringEmployeeStatus
{
    Active,
    Idle,
    Offline
}

public sealed record MonitoringDashboardDto(
    DateOnly Date,
    MonitoringDashboardSummaryDto Summary,
    IReadOnlyList<MonitoringEmployeeDashboardItemDto> Employees,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record MonitoringDashboardSummaryDto(
    int TotalEmployees,
    int ActiveEmployees,
    int IdleEmployees,
    int OfflineEmployees,
    int AttentionNeededEmployees,
    decimal? AverageActivityScore);

public sealed record MonitoringEmployeeDashboardItemDto(
    Guid EmployeeId,
    string EmployeeNumber,
    string FullName,
    string Email,
    string? DepartmentName,
    string? PositionName,
    MonitoringEmployeeStatus Status,
    DateTimeOffset? LastCapturedAt,
    int ActiveMinutes,
    int IdleMinutes,
    decimal? ActivityScore,
    decimal? DataCoveragePercentage,
    IReadOnlyList<AppUsageSummary> TopApps,
    IReadOnlyList<MonitoringDashboardAlertDto> Alerts);

public sealed record MonitoringDashboardAlertDto(
    string Code,
    string Message,
    string Severity);
