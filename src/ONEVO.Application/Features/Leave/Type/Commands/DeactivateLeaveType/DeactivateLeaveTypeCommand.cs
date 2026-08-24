using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Leave.Type.Commands.DeactivateLeaveType;

public record DeactivateLeaveTypeCommand(Guid LeaveTypeId, bool Confirmed) : IRequest<Result>;
