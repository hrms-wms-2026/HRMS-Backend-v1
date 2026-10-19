using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.UserAccounts.RepositoryInterfaces;

namespace ONEVO.Application.Features.Auth.UserAccounts.Queries.GetUserAccountSessions;

/// <summary>Every browser and tray app a user is signed in on, for Settings &gt; Users (users:read).</summary>
public sealed class GetUserAccountSessionsQueryHandler(
    IUserAccountRepository accounts,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetUserAccountSessionsQuery, Result<UserAccountSessionsResponse>>
{
    public async Task<Result<UserAccountSessionsResponse>> Handle(GetUserAccountSessionsQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var now = clock.UtcNow;

        if (await accounts.GetAsync(tenantId, request.UserId, now, ct) is null)
            return Result<UserAccountSessionsResponse>.NotFound("The user account could not be found.");

        var sessions = await accounts.ListActiveSessionsAsync(tenantId, request.UserId, now, ct);
        var devices = await accounts.ListActiveDevicesAsync(tenantId, request.UserId, ct);
        var currentSessionId = currentUser.SessionId;

        return Result<UserAccountSessionsResponse>.Success(new UserAccountSessionsResponse(
            sessions.Select(s => new UserWebSessionResponse(
                s.Id, UserAgentDescription.Describe(s.UserAgent), s.IpAddress,
                s.StartedAt, s.LastActivityAt, s.ExpiresAt, s.Id == currentSessionId)).ToList(),
            devices.Select(d => new UserTrayDeviceResponse(
                d.Id, d.DeviceName, d.DeviceOs, d.ActivatedAt, d.LastSeenAt)).ToList()));
    }
}
