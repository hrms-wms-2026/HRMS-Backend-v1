using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.UserAccounts.RepositoryInterfaces;

namespace ONEVO.Application.Features.Auth.UserAccounts.Commands.RevokeUserAccountSession;

/// <summary>
/// Revokes a single sign-in (users:manage) - the rest of the user's sessions keep working and the
/// account stays active. A web session ends on its next request (TenantDatabaseTicketStore checks
/// IsRevoked); a tray app drops once its current access token expires and the refresh is refused.
/// Works on your own account too, except the session you are using right now.
/// </summary>
public sealed class RevokeUserAccountSessionCommandHandler(
    IUserAccountRepository accounts,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<RevokeUserAccountSessionCommand, Result>
{
    public async Task<Result> Handle(RevokeUserAccountSessionCommand request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var now = clock.UtcNow;

        if (request.SessionId is null == request.DeviceId is null)
            return Result.Failure("Choose one session or one device to revoke.", 400);

        if (await accounts.GetAsync(tenantId, request.UserId, now, ct) is null)
            return Result.NotFound("The user account could not be found.");

        if (request.SessionId is Guid sessionId)
        {
            if (sessionId == currentUser.SessionId)
                return Result.Conflict("This is the session you're using. Use Sign out instead.");

            return await accounts.RevokeSessionAsync(tenantId, request.UserId, sessionId, ct)
                ? Result.Success()
                : Result.NotFound("That session has already ended.");
        }

        return await accounts.RevokeDeviceAsync(tenantId, request.UserId, request.DeviceId!.Value, now, "revoked_by_admin", ct)
            ? Result.Success()
            : Result.NotFound("That device is already disconnected.");
    }
}
