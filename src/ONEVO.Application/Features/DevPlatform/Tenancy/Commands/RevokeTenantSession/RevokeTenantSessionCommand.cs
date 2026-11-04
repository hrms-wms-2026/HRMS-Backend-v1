using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.RevokeTenantSession;

public sealed record RevokeTenantSessionCommand(Guid TenantId, Guid SessionId) : IRequest<Result>, IPlatformAuditedCommand<Result>
{
    public AuditEntry DescribeAudit(Result response) => new()
    {
        Action = "tenant_session.revoked",
        TargetType = "tenant_session",
        TargetId = SessionId.ToString(),
        TargetTenantId = TenantId,
    };
}
