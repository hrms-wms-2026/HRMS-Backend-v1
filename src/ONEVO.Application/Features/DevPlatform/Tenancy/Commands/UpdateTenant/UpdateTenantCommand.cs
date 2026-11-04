using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.UpdateTenant;

public sealed record UpdateTenantCommand(
    Guid TenantId,
    string? Name,
    string? Slug,
    string? IndustryProfile) : IRequest<Result>, IPlatformAuditedCommand<Result>
{
    // The previous name, slug and industry profile are recorded by the handler.
    public AuditEntry DescribeAudit(Result response) => new()
    {
        Action = "tenant.updated",
        TargetType = "tenant",
        TargetId = TenantId.ToString(),
        TargetTenantId = TenantId,
        After = new { name = Name, slug = Slug, industryProfile = IndustryProfile },
    };
}
