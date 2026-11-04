using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;
using ONEVO.Application.Common.Auditing;


namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.AdminCreateTenantRole;

public sealed record AdminCreateTenantRoleCommand(
    Guid TenantId,
    string Name,
    string? Description,
    IReadOnlyList<Guid>? PermissionIds) : IRequest<Result<RoleDetailDto>>, IPlatformAuditedCommand<Result<RoleDetailDto>>
{
    public AuditEntry DescribeAudit(Result<RoleDetailDto> response) => new()
    {
        Action = "tenant_role.created",
        TargetType = "tenant_role",
        TargetId = response.Value?.Id.ToString(),
        TargetTenantId = TenantId,
        After = new
        {
            name = Name,
            description = Description,
            permissionIds = (PermissionIds ?? Array.Empty<Guid>()).Distinct().OrderBy(id => id).ToArray(),
        },
    };
}
