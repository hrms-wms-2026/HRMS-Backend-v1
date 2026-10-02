using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Mappers;

public static class WorkTaskResponseMapper
{
    public static WorkTaskResponse ToResponse(WorkTask task, IReadOnlyList<Guid>? assigneeIds = null) => new(
        task.Id, task.ObjectiveId, task.ShortId, task.Title, task.Description,
        task.CategoryId, task.StatusId, task.Priority, task.StoryPoints,
        task.DueDate, task.EstimatedHours, task.CompletedHours, task.ProgressPercent, task.SprintId,
        assigneeIds, CreatedAt: task.CreatedAt);
}
