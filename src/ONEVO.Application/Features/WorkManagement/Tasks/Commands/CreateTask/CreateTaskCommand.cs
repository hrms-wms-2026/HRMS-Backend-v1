using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTask;

public sealed record CreateTaskCommand(
    Guid ObjectiveId, string Title, string? Description, Guid CategoryId, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, Guid? SprintId,
    IReadOnlyList<Guid>? AttachmentFileIds = null
) : IRequest<Result<WorkTaskResponse>>;
