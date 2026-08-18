using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskStatus;

public sealed record DeleteTaskStatusCommand(Guid StatusId) : IRequest<Result>;
