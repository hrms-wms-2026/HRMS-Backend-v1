using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.InvitePlatformManager;

public record InvitePlatformManagerCommand(
    string Email,
    string FullName,
    IReadOnlyList<Guid> RoleIds) : IRequest<Result>, IPlatformAuditedCommand<Result>
{
    // Target is the new platform user, recorded by the handler. The invite token is never included.
    public AuditEntry DescribeAudit(Result response) => new()
    {
        Action = "platform_user.invited",
        TargetType = "platform_user",
        After = new { roleIds = RoleIds.Distinct().OrderBy(id => id).ToArray() },
    };
}
