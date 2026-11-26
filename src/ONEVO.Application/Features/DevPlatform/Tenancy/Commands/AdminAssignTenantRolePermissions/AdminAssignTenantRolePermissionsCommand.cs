using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;
using ONEVO.Application.Common.Auditing;


namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.AdminAssignTenantRolePermissions;

public sealed record AdminAssignTenantRolePermissionsCommand(
    Guid TenantId,
    Guid RoleId,
    IReadOnlyList<Guid> PermissionIds) : IRequest<Result<RoleDetailDto>>, IAuditedCommand<Result<RoleDetailDto>>
{
    public AuditEntry DescribeAudit(Result<RoleDetailDto>? response)
        => AuditEntry.For("tenant_role.permissions_updated", "tenant_role", RoleId, TenantId, new { PermissionIds });
}
