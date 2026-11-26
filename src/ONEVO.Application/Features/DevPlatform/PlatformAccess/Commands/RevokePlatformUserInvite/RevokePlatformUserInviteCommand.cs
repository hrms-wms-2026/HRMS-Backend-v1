using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserInvite;

public record RevokePlatformUserInviteCommand(Guid PlatformUserId) : IRequest<Result>, IAuditedCommand<Result>
{
    public AuditEntry DescribeAudit(Result? response)
        => AuditEntry.For("platform_user.invite_revoked", "platform_user", PlatformUserId);
}
