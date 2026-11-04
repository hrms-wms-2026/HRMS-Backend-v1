using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserInvite;

public record RevokePlatformUserInviteCommand(Guid PlatformUserId) : IRequest<Result>, IPlatformAuditedCommand<Result>
{
    public AuditEntry DescribeAudit(Result response) => new()
    {
        Action = "platform_user.invite_revoked",
        TargetType = "platform_user",
        TargetId = PlatformUserId.ToString(),
    };
}
