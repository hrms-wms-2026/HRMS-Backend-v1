using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.Auth.Roles.Commands.ArchiveRole;

public record ArchiveRoleCommand(Guid RoleId) : IRequest<Result>, IAuditedCommand<Result>
{
    public AuditEntry DescribeAudit(Result? response)
        => AuditEntry.For("role.archived", "role", RoleId);
}
