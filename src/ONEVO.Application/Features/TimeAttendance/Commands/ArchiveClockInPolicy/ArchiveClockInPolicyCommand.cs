using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ArchiveClockInPolicy;

public record ArchiveClockInPolicyCommand(Guid LegalEntityId, Guid PolicyId)
    : IRequest<Result<bool>>;
