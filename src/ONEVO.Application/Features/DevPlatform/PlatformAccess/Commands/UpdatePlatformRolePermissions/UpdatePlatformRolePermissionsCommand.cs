using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.UpdatePlatformRolePermissions;

public record UpdatePlatformRolePermissionsCommand(
    Guid RoleId,
    IReadOnlyList<string> Permissions) : IRequest, IAuditedCommand<Unit>
{
    public AuditEntry DescribeAudit(Unit response)
        => AuditEntry.For("platform_role.permissions_updated", "platform_role", RoleId, null, new { Permissions });
}
