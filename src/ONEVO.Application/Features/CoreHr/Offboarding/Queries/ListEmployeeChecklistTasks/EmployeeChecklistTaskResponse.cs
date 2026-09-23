namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListEmployeeChecklistTasks;

public sealed record EmployeeChecklistTaskResponse(
    Guid Id, string TaskTitle, string OwnerType, Guid AssignedToId, DateOnly DueDate, bool IsRequired,
    bool IsBypassable, string? BypassPenaltyDescription, string? Category, string Status, DateTimeOffset? CompletedAt);
