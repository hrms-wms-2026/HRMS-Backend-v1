using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;
using ONEVO.Application.Common.Auditing;


namespace ONEVO.Application.Features.Auth.Roles.Commands.AssignRolePermissions;

public record AssignRolePermissionsCommand(
    Guid RoleId,
    IReadOnlyList<Guid> PermissionIds) : IRequest<Result<RoleDetailDto>>, IAuditedCommand<Result<RoleDetailDto>>
{
    public AuditEntry DescribeAudit(Result<RoleDetailDto>? response)
        => AuditEntry.For("role.permissions_updated", "role", RoleId, null, new { PermissionIds });
}
