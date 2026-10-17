namespace ONEVO.Application.Features.Auth.UserAccounts.RepositoryInterfaces;

/// <summary>One tenant user account with everything Settings &gt; Users needs to show its state.</summary>
public sealed record UserAccountRow(
    Guid UserId,
    string FirstName,
    string LastName,
    string Email,
    bool IsActive,
    DateTimeOffset? SuspendedAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    Guid? EmployeeId,
    int? EmploymentStatusId,
    IReadOnlyList<string> RoleNames,
    bool HoldsOwnerRole,
    DateTimeOffset? InvitationExpiresAt,
    DateTimeOffset? InvitationUsedAt,
    DateTimeOffset? InvitationRevokedAt,
    int ActiveSessionCount,
    int ActiveDeviceCount);

/// <summary>One signed-in web browser (a sessions row).</summary>
public sealed record UserWebSessionRow(
    Guid Id, string IpAddress, string UserAgent, DateTimeOffset StartedAt, DateTimeOffset LastActivityAt, DateTimeOffset ExpiresAt);

/// <summary>One connected tray app install (an active tray device registration).</summary>
public sealed record UserTrayDeviceRow(
    Guid Id, string DeviceName, string DeviceOs, DateTimeOffset ActivatedAt, DateTimeOffset? LastSeenAt);

public interface IUserAccountRepository
{
    Task<IReadOnlyList<UserAccountRow>> ListAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct = default);

    Task<UserAccountRow?> GetAsync(Guid tenantId, Guid userId, DateTimeOffset now, CancellationToken ct = default);

    Task<IReadOnlyList<UserWebSessionRow>> ListActiveSessionsAsync(Guid tenantId, Guid userId, DateTimeOffset now, CancellationToken ct = default);

    Task<IReadOnlyList<UserTrayDeviceRow>> ListActiveDevicesAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Ends one web session of this user. False when it isn't theirs or is already ended.</summary>
    Task<bool> RevokeSessionAsync(Guid tenantId, Guid userId, Guid sessionId, CancellationToken ct = default);

    /// <summary>Disconnects one tray device of this user (registration + its refresh tokens).</summary>
    Task<bool> RevokeDeviceAsync(Guid tenantId, Guid userId, Guid deviceId, DateTimeOffset now, string reason, CancellationToken ct = default);

    /// <summary>
    /// Ends every way the user is signed in: web sessions, web refresh tokens, and tray device
    /// registrations with their refresh tokens. Returns how many sessions/devices were ended.
    /// Tray access tokens are self-contained JWTs, so a tray stays connected until its current
    /// access token expires and the refresh is refused.
    /// </summary>
    Task<int> RevokeAllAccessAsync(Guid tenantId, Guid userId, DateTimeOffset now, string reason, CancellationToken ct = default);
}
