namespace ONEVO.Application.Features.WorkManagement.Monitoring.DTOs;

/// <summary>An advisory warning shown while planning. It never blocks saving.</summary>
public sealed record MonitorWarning(string Code, string Message, Guid? EmployeeId = null);

public sealed record ModuleCapacityCheckResponse(
    decimal DailyHours, int WorkingDays, int MemberCount, decimal CapacityHours, decimal AllocatedHours,
    IReadOnlyList<MonitorWarning> Warnings);

public sealed record TaskLoadCheckResponse(IReadOnlyList<MonitorWarning> Warnings);

public sealed record MonitorAlertResponse(
    Guid Id, string TargetType, Guid TargetId, string TargetTitle, string RuleCode, Guid? SubjectEmployeeId,
    string Message, DateTimeOffset FirstDetectedAt);
