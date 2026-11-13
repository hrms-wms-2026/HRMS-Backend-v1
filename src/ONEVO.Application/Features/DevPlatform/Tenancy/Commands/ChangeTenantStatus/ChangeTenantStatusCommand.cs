using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ChangeTenantStatus;

public sealed record ChangeTenantStatusCommand(
    Guid TenantId,
    string Action,
    string? Reason) : IRequest<Result>, IAuditedCommand<Result>
{
    public AuditEntry DescribeAudit(Result? response)
        => AuditEntry.For("tenant.status_changed", "tenant", TenantId, TenantId, new { Action, Reason });
}
