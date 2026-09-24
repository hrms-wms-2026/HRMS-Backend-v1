using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Api.Contracts.WorkManagement.Tasks;

public sealed record CreateTaskCreationRequestRequest(
    string Title, string? Description, Guid CategoryId, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, Guid? SprintId);

public sealed record RejectTaskCreationRequestRequest(string Comment);

public sealed record TaskCreationRequestViewModel(
    Guid Id, Guid ObjectiveId, string Status, TaskCreationRequestPayload Payload, DateTimeOffset CreatedAt);

public static class TaskCreationRequestViewModelMapper
{
    public static TaskCreationRequestViewModel ToViewModel(this TaskCreationRequestResponse dto) =>
        new(dto.Id, dto.ObjectiveId, dto.Status, dto.Payload, dto.CreatedAt);
}
