namespace ONEVO.Application.Features.WorkManagement.Monitoring.DTOs;

/// <summary>An advisory warning shown while planning. It never blocks saving.</summary>
public sealed record MonitorWarning(string Code, string Message, Guid? EmployeeId = null);

/// <param name="ConversionRate">The module team's pooled 4-week task rate; null = no history (capacity assumed at 100%).</param>
/// <param name="PredictedHours">CapacityHours x the rate (x 1 without history): what the team will really complete.</param>
public sealed record ModuleCapacityCheckResponse(
    decimal DailyHours, int WorkingDays, int MemberCount, decimal CapacityHours, decimal AllocatedHours,
    IReadOnlyList<MonitorWarning> Warnings, decimal? ConversionRate = null, decimal? PredictedHours = null);

public sealed record TaskLoadCheckResponse(IReadOnlyList<MonitorWarning> Warnings);

/// <summary>InAchievedModule: the target is an achieved Module or sits under one - shown for
/// information only (faded, not counted, never notified).</summary>
public sealed record MonitorAlertResponse(
    Guid Id, string TargetType, Guid TargetId, string TargetTitle, string RuleCode, Guid? SubjectEmployeeId,
    string Message, DateTimeOffset FirstDetectedAt, bool InAchievedModule = false);
