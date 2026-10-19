using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Auth.UserAccounts.Queries.GetUserAccountSessions;

public record GetUserAccountSessionsQuery(Guid UserId) : IRequest<Result<UserAccountSessionsResponse>>;

public record UserAccountSessionsResponse(
    IReadOnlyList<UserWebSessionResponse> WebSessions,
    IReadOnlyList<UserTrayDeviceResponse> TrayDevices);

public record UserWebSessionResponse(
    Guid Id,
    /// <summary>"Chrome on Windows" - best effort from the user agent.</summary>
    string Device,
    string IpAddress,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt,
    DateTimeOffset ExpiresAt,
    /// <summary>The session the caller is using right now - end it with the normal sign-out.</summary>
    bool IsCurrent);

public record UserTrayDeviceResponse(
    Guid Id,
    string DeviceName,
    string DeviceOs,
    DateTimeOffset ActivatedAt,
    DateTimeOffset? LastSeenAt);
