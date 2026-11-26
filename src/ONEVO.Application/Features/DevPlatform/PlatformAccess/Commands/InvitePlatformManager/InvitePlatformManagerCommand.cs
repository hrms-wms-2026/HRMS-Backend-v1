using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.InvitePlatformManager;

public record InvitePlatformManagerCommand(
    string Email,
    string FullName,
    IReadOnlyList<Guid> RoleIds) : IRequest<Result>, IAuditedCommand<Result>
{
    public AuditEntry DescribeAudit(Result? response)
        => new("platform_user.invited", "platform_user", Email, null, new { Email, FullName, RoleIds });
}
