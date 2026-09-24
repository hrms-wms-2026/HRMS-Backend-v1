using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.UnassignTask;

public sealed record UnassignTaskCommand(Guid TaskId, Guid EmployeeId) : IRequest<Result>;
