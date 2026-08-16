using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Api.Contracts.WorkManagement.Tasks;

public static class WorkTaskViewModelMapper
{
    public static WorkTaskViewModel ToViewModel(this WorkTaskResponse dto) => new(
        dto.Id, dto.ObjectiveId, dto.ShortId, dto.Title, dto.Description,
        dto.TaskType, dto.StatusId, dto.Priority, dto.StoryPoints,
        dto.DueDate, dto.EstimatedHours, dto.CompletedHours, dto.ProgressPercent);

    public static TaskStatusViewModel ToViewModel(this TaskStatusResponse dto) => new(
        dto.Id, dto.Name, dto.DisplayOrder, dto.RequiresApproval, dto.ApproverId, dto.MarksTaskComplete);
}
