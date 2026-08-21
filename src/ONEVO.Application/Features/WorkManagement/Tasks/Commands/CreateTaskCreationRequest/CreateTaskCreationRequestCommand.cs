using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskCreationRequest;

public sealed record CreateTaskCreationRequestCommand(
    Guid ObjectiveId, string Title, string? Description, string TaskType, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, Guid? SprintId
) : IRequest<Result<TaskCreationRequestResponse>>;
