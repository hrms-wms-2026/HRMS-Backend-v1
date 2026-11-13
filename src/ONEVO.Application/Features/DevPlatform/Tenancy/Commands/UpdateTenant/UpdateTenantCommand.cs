using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.UpdateTenant;

public sealed record UpdateTenantCommand(
    Guid TenantId,
    string? Name,
    string? Slug,
    string? IndustryProfile) : IRequest<Result>, IAuditedCommand<Result>
{
    public AuditEntry DescribeAudit(Result? response)
        => AuditEntry.For("tenant.updated", "tenant", TenantId, TenantId, new { Name, Slug, IndustryProfile });
}
