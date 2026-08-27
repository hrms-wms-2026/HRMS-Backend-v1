using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Api.Contracts.WorkManagement.Tasks;

public sealed record CreateTaskRequest(
    string Title, string? Description, Guid CategoryId, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, Guid? SprintId);

public sealed record EditTaskRequest(
    string Title, string? Description, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints);

public sealed record CreateTaskEditRequestRequest(
    string Title, string? Description, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints);

public sealed record RejectTaskEditRequestRequest(string Comment);

public sealed record MoveTaskStatusRequest(Guid NewStatusId);

public sealed record AssignTaskRequest(Guid EmployeeId);

public sealed record EditTaskStatusRequest(
    string Name, int DisplayOrder, bool RequiresApproval, Guid? ApproverId, string Visibility);

public sealed record CreateTaskStatusRequest(
    string Name, int DisplayOrder, string Visibility, bool MarksTaskComplete, bool RequiresApproval, Guid? ApproverId);

public sealed record TaskStatusOrderUpdateRequest(
    Guid StatusId, int DisplayOrder, string Visibility, bool MarksTaskComplete);

public sealed record ReorderTaskStatusesRequest(List<TaskStatusOrderUpdateRequest> Updates);

public sealed record EditTaskCategoryRequest(string Name, int DisplayOrder);

public sealed record CreateTaskCategoryRequest(string Name, int DisplayOrder);

public sealed record TaskCategoryOrderUpdateRequest(Guid CategoryId, int DisplayOrder);

public sealed record ReorderTaskCategoriesRequest(List<TaskCategoryOrderUpdateRequest> Updates);

public sealed record TaskCategoryViewModel(Guid Id, string Name, int DisplayOrder);

public sealed record WorkTaskViewModel(
    Guid Id, Guid ObjectiveId, string ShortId, string Title, string? Description,
    Guid CategoryId, Guid StatusId, string Priority, int? StoryPoints,
    DateOnly? DueDate, decimal? EstimatedHours, decimal CompletedHours, int ProgressPercent,
    Guid? SprintId, IReadOnlyList<Guid> AssigneeEmployeeIds);

public sealed record TaskStatusViewModel(
    Guid Id, string Name, int DisplayOrder, bool RequiresApproval,
    Guid? ApproverId, bool MarksTaskComplete, string Visibility);

public sealed record TaskEditRequestViewModel(
    Guid Id, Guid TaskId, string Status, TaskEditRequestPayload Payload,
    string RequestedByName, DateTimeOffset CreatedAt);

public static class TaskEditRequestViewModelMapper
{
    public static TaskEditRequestViewModel ToViewModel(this TaskEditRequestResponse dto) =>
        new(dto.Id, dto.TaskId, dto.Status, dto.Payload, dto.RequestedByName, dto.CreatedAt);
}

public sealed record ObjectiveDeadlineViewModel(Guid ObjectiveId, string Title, DateOnly EndDate);

public sealed record TaskDeadlineViewModel(Guid TaskId, string ShortId, string Title, DateOnly DueDate);

public sealed record MyDeadlinesViewModel(
    IReadOnlyList<ObjectiveDeadlineViewModel> ObjectiveDeadlines,
    IReadOnlyList<TaskDeadlineViewModel> TaskDeadlines);

public sealed record WorkNotificationNavigationViewModel(
    Guid ProjectId, Guid ObjectiveId, Guid? TaskId, string TargetTab);

public sealed record MyTaskItemViewModel(
    Guid TaskId, string ShortId, string Title, DateOnly DueDate, bool IsOverdue,
    Guid ProjectId, string ProjectName, Guid ObjectiveId, string Priority);

public sealed record MyTasksViewModel(IReadOnlyList<MyTaskItemViewModel> Tasks);

public static class MyTasksViewModelMapper
{
    public static MyTasksViewModel ToViewModel(this MyTasksResponse dto) =>
        new(dto.Tasks.Select(t => new MyTaskItemViewModel(
            t.TaskId, t.ShortId, t.Title, t.DueDate, t.IsOverdue,
            t.ProjectId, t.ProjectName, t.ObjectiveId, t.Priority)).ToList());
}

public sealed record TaskProgressViewModel(
    int Completed, int InProgress, int NotStarted, int Overdue, int Total);

public static class TaskProgressViewModelMapper
{
    public static TaskProgressViewModel ToViewModel(this TaskProgressResponse dto) =>
        new(dto.Completed, dto.InProgress, dto.NotStarted, dto.Overdue, dto.Total);
}
