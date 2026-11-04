using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;
using ONEVO.Application.Common.Auditing;


namespace ONEVO.Application.Features.Auth.Roles.Commands.AssignRolePermissions;

public record AssignRolePermissionsCommand(
    Guid RoleId,
    IReadOnlyList<Guid> PermissionIds) : IRequest<Result<RoleDetailDto>>, ITenantAuditedCommand<Result<RoleDetailDto>>
{
    // Before-state (the previous permission ids) is recorded by the handler.
    public AuditEntry DescribeAudit(Result<RoleDetailDto> response) => new()
    {
        Action = "role.permissions_assigned",
        TargetType = "role",
        TargetId = RoleId.ToString(),
        After = new { permissionIds = PermissionIds.Distinct().OrderBy(id => id).ToArray() },
    };
}
