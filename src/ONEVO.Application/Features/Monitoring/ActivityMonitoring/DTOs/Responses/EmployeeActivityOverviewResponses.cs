namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

public sealed record EmployeeActivityMetrics(
    int ActiveMinutes,
    int IdleMinutes,
    int MeetingMinutes,
    int DaysWithData);

/// <summary>Sums of the persisted daily activity summaries in the period. When activity monitoring
/// is not enabled for the employee, ActivityMonitoringEnabled is false and everything is zero.</summary>
public sealed record EmployeeActivityOverviewResponse(
    DateOnly From,
    DateOnly To,
    bool ActivityMonitoringEnabled,
    int ActiveMinutes,
    int IdleMinutes,
    int MeetingMinutes,
    int DaysWithData,
    EmployeeActivityMetrics? Previous);
