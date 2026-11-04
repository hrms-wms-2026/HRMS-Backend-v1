using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;
using ONEVO.Application.Common.Auditing;


namespace ONEVO.Application.Features.Auth.Roles.Commands.UpdateRole;

public record UpdateRoleCommand(
    Guid RoleId,
    string Name,
    string? Description) : IRequest<Result<RoleSummaryDto>>, ITenantAuditedCommand<Result<RoleSummaryDto>>
{
    // Before-state (previous name and description) is recorded by the handler.
    public AuditEntry DescribeAudit(Result<RoleSummaryDto> response) => new()
    {
        Action = "role.updated",
        TargetType = "role",
        TargetId = RoleId.ToString(),
        After = new { name = response.Value?.Name, description = response.Value?.Description },
    };
}
