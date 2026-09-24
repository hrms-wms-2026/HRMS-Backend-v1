using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.PushTask;

public sealed record PushTaskCommand(Guid TaskId, int Percent, string? Reason) : IRequest<Result<WorkTaskResponse>>;
