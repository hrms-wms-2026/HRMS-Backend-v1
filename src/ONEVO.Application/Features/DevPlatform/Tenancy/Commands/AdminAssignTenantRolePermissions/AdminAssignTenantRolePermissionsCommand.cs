using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;
using ONEVO.Application.Common.Auditing;


namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.AdminAssignTenantRolePermissions;

public sealed record AdminAssignTenantRolePermissionsCommand(
    Guid TenantId,
    Guid RoleId,
    IReadOnlyList<Guid> PermissionIds) : IRequest<Result<RoleDetailDto>>, IPlatformAuditedCommand<Result<RoleDetailDto>>
{
    // Before-state (the previous permission ids) is recorded by the handler.
    public AuditEntry DescribeAudit(Result<RoleDetailDto> response) => new()
    {
        Action = "tenant_role.permissions_assigned",
        TargetType = "tenant_role",
        TargetId = RoleId.ToString(),
        TargetTenantId = TenantId,
        After = new { permissionIds = PermissionIds.Distinct().OrderBy(id => id).ToArray() },
    };
}
