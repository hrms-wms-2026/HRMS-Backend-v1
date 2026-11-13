using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ApplyRoleTemplate;

public sealed record ApplyRoleTemplateCommand(
    Guid TenantId,
    Guid TemplateId,
    string? RoleNameOverride,
    bool ForceUpdate) : IRequest<Result<ApplyRoleTemplateResultDto>>, IAuditedCommand<Result<ApplyRoleTemplateResultDto>>
{
    public AuditEntry DescribeAudit(Result<ApplyRoleTemplateResultDto>? response)
        => AuditEntry.For("role_template.applied_to_tenant", "role_template", TemplateId, TenantId, new { RoleNameOverride, ForceUpdate, RoleId = response?.Value?.RoleId, AppliedPermissions = response?.Value?.AppliedPermissions });
}
