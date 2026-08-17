using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;

public sealed record ChangeEmployeePositionCommand(Guid EmployeeId, Guid PositionId, DateOnly EffectiveFrom, string ChangeReason)
    : IRequest<Result<ChangeEmployeePositionResponse>>;
