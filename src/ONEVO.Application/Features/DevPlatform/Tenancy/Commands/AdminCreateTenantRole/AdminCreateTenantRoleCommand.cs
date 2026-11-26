using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;
using ONEVO.Application.Common.Auditing;


namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.AdminCreateTenantRole;

public sealed record AdminCreateTenantRoleCommand(
    Guid TenantId,
    string Name,
    string? Description,
    IReadOnlyList<Guid>? PermissionIds) : IRequest<Result<RoleDetailDto>>, IAuditedCommand<Result<RoleDetailDto>>
{
    public AuditEntry DescribeAudit(Result<RoleDetailDto>? response)
        => AuditEntry.For("tenant_role.created", "tenant_role", response?.Value?.Id, TenantId, new { Name, Description, PermissionIds });
}
