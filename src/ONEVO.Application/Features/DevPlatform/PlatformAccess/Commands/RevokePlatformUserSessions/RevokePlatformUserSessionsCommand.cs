using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserSessions;

public record RevokePlatformUserSessionsCommand(Guid UserId) : IRequest, IAuditedCommand<Unit>
{
    public AuditEntry DescribeAudit(Unit response)
        => AuditEntry.For("platform_user.sessions_revoked", "platform_user", UserId);
}
