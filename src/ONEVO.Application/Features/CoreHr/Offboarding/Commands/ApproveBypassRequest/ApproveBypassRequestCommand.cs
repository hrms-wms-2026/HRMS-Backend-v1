using MediatR;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.ApproveBypassRequest;

public sealed record ApproveBypassRequestCommand(Guid BypassRequestId) : IRequest<Result>, IAuditedCommand<Result>
{
    public AuditEntry DescribeAudit(Result? response)
        => AuditEntry.For("offboarding.bypass_approved", "bypass_request", BypassRequestId);
}
