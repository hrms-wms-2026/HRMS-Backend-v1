using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.UpdatePlatformUserRoles;

public record UpdatePlatformUserRolesCommand(
    Guid UserId,
    IReadOnlyList<Guid> RoleIds) : IRequest, IAuditedCommand<Unit>
{
    public AuditEntry DescribeAudit(Unit response)
        => AuditEntry.For("platform_user.roles_updated", "platform_user", UserId, null, new { RoleIds });
}
