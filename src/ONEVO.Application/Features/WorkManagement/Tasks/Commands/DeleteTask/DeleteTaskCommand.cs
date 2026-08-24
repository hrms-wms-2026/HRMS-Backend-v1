using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTask;

public sealed record DeleteTaskCommand(Guid TaskId) : IRequest<Result>;
