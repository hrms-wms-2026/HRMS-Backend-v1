using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ConvertTaskToSubtask;

public sealed record ConvertTaskToSubtaskCommand(
    Guid TaskId, Guid NewParentTaskId
) : IRequest<Result<WorkTaskResponse>>;
