using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserSession;

public record RevokePlatformUserSessionCommand(Guid UserId, Guid SessionId) : IRequest, IAuditedCommand<Unit>
{
    public AuditEntry DescribeAudit(Unit response)
        => AuditEntry.For("platform_user.session_revoked", "platform_user", UserId, null, new { SessionId });
}
