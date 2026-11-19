namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

/// <summary>Bypassed tasks are counted in Total but not in Completed.</summary>
public sealed record EmployeeChecklistTaskOverviewItem(
    Guid Id,
    string Title,
    Guid AssignedToEmployeeId,
    string AssigneeName,
    DateOnly DueDate,
    string Status,
    DateTimeOffset? CompletedAt,
    string? CompletedBy,
    Guid? WorkTaskId,
    string? WorkTaskShortId,
    Guid? WorkProjectId,
    bool CanOpenTask);

public sealed record EmployeeChecklistGroup(
    string Name,
    string LifecycleType,
    int Completed,
    int Bypassed,
    int Total,
    IReadOnlyList<EmployeeChecklistTaskOverviewItem>? Tasks = null);

public sealed record EmployeeChecklistOverviewResponse(IReadOnlyList<EmployeeChecklistGroup> Groups);
