using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteEmployeeChecklistTask;

public sealed record CompleteEmployeeChecklistTaskCommand(Guid EmployeeId, Guid TaskId) : IRequest<Result>;
