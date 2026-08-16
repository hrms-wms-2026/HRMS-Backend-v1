namespace ONEVO.Api.Contracts.WorkManagement.Tasks;

public sealed record CreateTaskRequest(
    string Title, string? Description, string TaskType, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints);

public sealed record EditTaskRequest(
    string Title, string? Description, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints);

public sealed record MoveTaskStatusRequest(Guid NewStatusId);

public sealed record AssignTaskRequest(Guid EmployeeId);

public sealed record EditTaskStatusRequest(
    string Name, int DisplayOrder, bool RequiresApproval, Guid? ApproverId);

public sealed record WorkTaskViewModel(
    Guid Id, Guid ObjectiveId, string ShortId, string Title, string? Description,
    string TaskType, Guid StatusId, string Priority, int? StoryPoints,
    DateOnly? DueDate, decimal? EstimatedHours, decimal CompletedHours, int ProgressPercent);

public sealed record TaskStatusViewModel(
    Guid Id, string Name, int DisplayOrder, bool RequiresApproval, Guid? ApproverId, bool MarksTaskComplete);
