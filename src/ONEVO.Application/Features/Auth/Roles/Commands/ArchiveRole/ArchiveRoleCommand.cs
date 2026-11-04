using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.Auth.Roles.Commands.ArchiveRole;

public record ArchiveRoleCommand(Guid RoleId) : IRequest<Result>, ITenantAuditedCommand<Result>
{
    // Before-state (the role name) is recorded by the handler.
    public AuditEntry DescribeAudit(Result response) => new()
    {
        Action = "role.archived",
        TargetType = "role",
        TargetId = RoleId.ToString(),
    };
}
