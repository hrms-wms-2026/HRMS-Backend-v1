using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;
using ONEVO.Application.Common.Auditing;


namespace ONEVO.Application.Features.Auth.Roles.Commands.CreateRole;

public record CreateRoleCommand(
    string Name,
    string? Description,
    IReadOnlyList<Guid>? PermissionIds) : IRequest<Result<RoleDetailDto>>, ITenantAuditedCommand<Result<RoleDetailDto>>
{
    public AuditEntry DescribeAudit(Result<RoleDetailDto> response) => new()
    {
        Action = "role.created",
        TargetType = "role",
        TargetId = response.Value?.Id.ToString(),
        After = new
        {
            name = Name,
            description = Description,
            permissionIds = (PermissionIds ?? Array.Empty<Guid>()).Distinct().OrderBy(id => id).ToArray(),
        },
    };
}
