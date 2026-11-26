using MediatR;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.UserAccounts.Commands.RevokeUserAccountSession;

/// <summary>Ends one web session (SessionId) or disconnects one tray app (DeviceId) of a user.</summary>
public record RevokeUserAccountSessionCommand(Guid UserId, Guid? SessionId, Guid? DeviceId)
    : IRequest<Result>, IAuditedCommand<Result>
{
    public AuditEntry DescribeAudit(Result? response)
        => AuditEntry.For("user.session_revoked", "user", UserId,
            details: new { sessionId = SessionId, deviceId = DeviceId });
}
