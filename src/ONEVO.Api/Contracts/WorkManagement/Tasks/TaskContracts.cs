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
    Guid Id, string Name, int DisplayOrder, bool RequiresApproval,
    Guid? ApproverId, bool MarksTaskComplete, string Visibility);

public sealed record ObjectiveDeadlineViewModel(Guid ObjectiveId, string Title, DateOnly EndDate);

public sealed record TaskDeadlineViewModel(Guid TaskId, string ShortId, string Title, DateOnly DueDate);

public sealed record MyDeadlinesViewModel(
    IReadOnlyList<ObjectiveDeadlineViewModel> ObjectiveDeadlines,
    IReadOnlyList<TaskDeadlineViewModel> TaskDeadlines);

public sealed record WorkNotificationNavigationViewModel(
    Guid ProjectId, Guid ObjectiveId, Guid? TaskId, string TargetTab);
