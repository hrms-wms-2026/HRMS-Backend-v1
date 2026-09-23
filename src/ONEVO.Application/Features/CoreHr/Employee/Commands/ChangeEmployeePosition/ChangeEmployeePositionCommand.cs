using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;

public sealed record ChangeEmployeePositionCommand(
    Guid EmployeeId, Guid PositionId, DateOnly EffectiveFrom, string ChangeReason, Guid? ReportsToEmployeeId = null)
    : IRequest<Result<ChangeEmployeePositionResponse>>;
