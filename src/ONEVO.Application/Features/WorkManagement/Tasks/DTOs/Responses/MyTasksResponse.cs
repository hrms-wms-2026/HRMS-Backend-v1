namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public sealed record MyTaskItem(
    Guid TaskId,
    string ShortId,
    string Title,
    DateOnly DueDate,
    bool IsOverdue,
    Guid ProjectId,
    string ProjectName,
    Guid ObjectiveId,
    string Priority);

public sealed record MyTasksResponse(IReadOnlyList<MyTaskItem> Tasks);
