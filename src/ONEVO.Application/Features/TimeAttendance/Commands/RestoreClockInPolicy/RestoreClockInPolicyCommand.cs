using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.TimeAttendance.Commands.RestoreClockInPolicy;

public record RestoreClockInPolicyCommand(Guid LegalEntityId, Guid PolicyId)
    : IRequest<Result<bool>>;
