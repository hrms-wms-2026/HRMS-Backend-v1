using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CreateBypassRequest;

public sealed record CreateBypassRequestCommand(
    Guid EmployeeId, Guid TaskId, Guid ApproverId, string BypassReason, string? PenaltyDescription) : IRequest<Result<Guid>>;
