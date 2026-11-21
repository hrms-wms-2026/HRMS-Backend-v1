using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;
using ONEVO.Application.Common.Auditing;


namespace ONEVO.Application.Features.Auth.Roles.Commands.UpdateRole;

public record UpdateRoleCommand(
    Guid RoleId,
    string Name,
    string? Description) : IRequest<Result<RoleSummaryDto>>, IAuditedCommand<Result<RoleSummaryDto>>
{
    public AuditEntry DescribeAudit(Result<RoleSummaryDto>? response)
        => AuditEntry.For("role.updated", "role", RoleId, null, new { Name, Description });
}
