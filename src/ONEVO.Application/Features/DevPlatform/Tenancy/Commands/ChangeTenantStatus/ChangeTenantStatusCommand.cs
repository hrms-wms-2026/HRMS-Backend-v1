using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ChangeTenantStatus;

public sealed record ChangeTenantStatusCommand(
    Guid TenantId,
    string Action,
    string? Reason) : IRequest<Result>, IPlatformAuditedCommand<Result>
{
    // The previous status is recorded by the handler.
    public AuditEntry DescribeAudit(Result response) => new()
    {
        Action = "tenant.status_changed",
        TargetType = "tenant",
        TargetId = TenantId.ToString(),
        TargetTenantId = TenantId,
        After = new { action = Action, reason = Reason },
    };
}
