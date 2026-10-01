using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTask;

public sealed record DeleteTaskCommand(Guid TaskId) : IRequest<Result<TaskWriteOutcome>>;
