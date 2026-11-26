using MediatR;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ArchiveClockInPolicy;

public record ArchiveClockInPolicyCommand(Guid LegalEntityId, Guid PolicyId)
    : IRequest<Result<bool>>, IAuditedCommand<Result<bool>>
{
    public AuditEntry DescribeAudit(Result<bool>? response)
        => AuditEntry.For("clock_in_policy.archived", "clock_in_policy", PolicyId,
            details: new { legalEntityId = LegalEntityId });
}
