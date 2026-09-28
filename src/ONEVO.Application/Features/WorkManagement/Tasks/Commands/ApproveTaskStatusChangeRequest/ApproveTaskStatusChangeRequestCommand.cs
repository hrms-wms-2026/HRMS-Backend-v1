using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ApproveTaskStatusChangeRequest;

public sealed record ApproveTaskStatusChangeRequestCommand(Guid RequestId)
    : IRequest<Result<IReadOnlyList<TaskStatusResponse>>>;
