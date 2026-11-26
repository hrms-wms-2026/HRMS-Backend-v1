using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.RevokeTenantSession;

public sealed record RevokeTenantSessionCommand(Guid TenantId, Guid SessionId) : IRequest<Result>, IAuditedCommand<Result>
{
    public AuditEntry DescribeAudit(Result? response)
        => AuditEntry.For("tenant.session_revoked", "tenant_session", SessionId, TenantId);
}
