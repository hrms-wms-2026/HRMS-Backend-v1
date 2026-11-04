using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ApplyRoleTemplate;

public sealed record ApplyRoleTemplateCommand(
    Guid TenantId,
    Guid TemplateId,
    string? RoleNameOverride,
    bool ForceUpdate) : IRequest<Result<ApplyRoleTemplateResultDto>>, IPlatformAuditedCommand<Result<ApplyRoleTemplateResultDto>>
{
    // When an existing role is updated, the handler records its previous permission ids.
    public AuditEntry DescribeAudit(Result<ApplyRoleTemplateResultDto> response) => new()
    {
        Action = "tenant_role.template_applied",
        TargetType = "tenant_role",
        TargetId = response.Value?.RoleId.ToString(),
        TargetTenantId = TenantId,
        After = new
        {
            templateId = TemplateId,
            roleName = response.Value?.RoleName,
            forceUpdate = ForceUpdate,
            appliedPermissions = response.Value?.AppliedPermissions,
            rejectedPermissions = response.Value?.RejectedPermissions,
        },
    };
}
