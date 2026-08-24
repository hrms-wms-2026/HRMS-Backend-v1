using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.UpdateEmployeeChecklistTask;

public sealed record UpdateEmployeeChecklistTaskCommand(
    Guid EmployeeId, Guid TaskId, Guid AssignedToId, DateOnly DueDate, bool IsRequired) : IRequest<Result>;
