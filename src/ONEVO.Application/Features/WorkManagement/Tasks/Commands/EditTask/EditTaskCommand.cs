using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTask;

public sealed record EditTaskCommand(
    Guid TaskId, string Title, string? Description, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints
) : IRequest<Result<WorkTaskResponse>>;
