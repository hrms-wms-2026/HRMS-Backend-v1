using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Api.Contracts.WorkManagement.Tasks;

public static class WorkTaskViewModelMapper
{
    public static WorkTaskViewModel ToViewModel(this WorkTaskResponse dto) => new(
        dto.Id, dto.ObjectiveId, dto.ShortId, dto.Title, dto.Description,
        dto.CategoryId, dto.StatusId, dto.Priority, dto.StoryPoints,
        dto.DueDate, dto.EstimatedHours, dto.CompletedHours, dto.ProgressPercent, dto.SprintId,
        dto.AssigneeEmployeeIds ?? Array.Empty<Guid>());

    public static TaskStatusViewModel ToViewModel(this TaskStatusResponse dto) => new(
        dto.Id, dto.Name, dto.DisplayOrder, dto.RequiresApproval,
        dto.ApproverId, dto.MarksTaskComplete, dto.Visibility);

    public static MyDeadlinesViewModel ToViewModel(this MyDeadlinesResponse dto) => new(
        dto.ObjectiveDeadlines.Select(o => new ObjectiveDeadlineViewModel(o.ObjectiveId, o.Title, o.EndDate)).ToList(),
        dto.TaskDeadlines.Select(t => new TaskDeadlineViewModel(t.TaskId, t.ShortId, t.Title, t.DueDate)).ToList());

    public static WorkNotificationNavigationViewModel ToViewModel(this WorkNotificationNavigationResponse dto) =>
        new(dto.ProjectId, dto.ObjectiveId, dto.TaskId, dto.TargetTab);
}
