using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.AssignTask;

public sealed record AssignTaskCommand(Guid TaskId, Guid EmployeeId) : IRequest<Result>;
