using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.MoveTaskStatus;

public sealed record MoveTaskStatusCommand(Guid TaskId, Guid NewStatusId) : IRequest<Result>;
