using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskEditRequest;

public sealed record CreateTaskEditRequestCommand(
    Guid TaskId, string Title, string? Description, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints
) : IRequest<Result<TaskEditRequestResponse>>;
