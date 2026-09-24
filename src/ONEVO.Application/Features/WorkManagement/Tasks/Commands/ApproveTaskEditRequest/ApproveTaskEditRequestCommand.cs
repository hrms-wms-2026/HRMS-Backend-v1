using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ApproveTaskEditRequest;

public sealed record ApproveTaskEditRequestCommand(Guid RequestId) : IRequest<Result<WorkTaskResponse>>;
