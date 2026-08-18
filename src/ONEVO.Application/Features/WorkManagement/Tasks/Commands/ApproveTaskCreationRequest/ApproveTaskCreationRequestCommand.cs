using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ApproveTaskCreationRequest;

public sealed record ApproveTaskCreationRequestCommand(Guid RequestId) : IRequest<Result<WorkTaskResponse>>;
