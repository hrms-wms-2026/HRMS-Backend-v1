using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;

public interface ITrayActivationRepository
{
    Task<int> CountRecentCodesForUserAsync(Guid userId, Guid tenantId, DateTimeOffset since, CancellationToken ct);
    Task AddActivationCodeAsync(TrayActivationCode code, CancellationToken ct);

    // Exchange endpoint: tenantId unknown at call time — hash is globally unique
    Task<TrayActivationCode?> FindActiveCodeByHashAsync(string codeHash, CancellationToken ct);
    Task MarkCodeUsedAsync(TrayActivationCode code, CancellationToken ct);
    Task RevokeActiveRegistrationsForIdentityAsync(
        Guid tenantId,
        Guid userId,
        string deviceFingerprint,
        string reason,
        CancellationToken ct);

    Task<int> CountRecentDeviceAuthorizationRequestsAsync(
        string deviceFingerprintHash, DateTimeOffset since, CancellationToken ct);
    Task AddDeviceAuthorizationAsync(TrayDeviceAuthorization authorization, CancellationToken ct);
    Task<TrayDeviceAuthorization?> FindDeviceAuthorizationForApprovalAsync(
        Guid requestId, string userCodeHash, CancellationToken ct);
    Task<TrayDeviceAuthorization?> LockDeviceAuthorizationForPollAsync(
        string deviceCodeHash, CancellationToken ct);

    Task AddDeviceRegistrationAsync(TrayDeviceRegistration device, CancellationToken ct);
    Task AddRefreshTokenAsync(TrayDeviceRefreshToken token, CancellationToken ct);

    Task<TrayDeviceRefreshToken?> FindActiveRefreshTokenAsync(string tokenHash, CancellationToken ct);

    /// <summary>
    /// A refresh token rotated at or after <paramref name="rotatedSince"/> whose replacement has not
    /// itself been used — i.e. the client most likely never received the rotation response. Null
    /// when the token is unknown, expired, revoked for another reason, or a later rotation exists.
    /// </summary>
    Task<TrayDeviceRefreshToken?> FindUnconsumedRotatedRefreshTokenAsync(
        string tokenHash, DateTimeOffset rotatedSince, CancellationToken ct);
    Task RevokeRefreshTokenAsync(TrayDeviceRefreshToken token, string reason, CancellationToken ct);
    Task RevokeAllRefreshTokensForDeviceAsync(Guid deviceRegistrationId, string reason, CancellationToken ct);

    Task<TrayDeviceRegistration?> FindActiveDeviceAsync(Guid deviceRegistrationId, Guid tenantId, CancellationToken ct);

    // Presence check (is the device currently online) — requires a heartbeat to have been recorded.
    Task<TrayDeviceRegistration?> FindLatestActiveDeviceForUserAsync(Guid userId, Guid tenantId, CancellationToken ct);

    // Identity check (does the user have an enrolled device at all, seen or not) — used to detect
    // enrollment from a second device immediately after the first device's initial activation,
    // before it has ever sent a heartbeat.
    Task<TrayDeviceRegistration?> FindActiveDeviceForUserAsync(Guid userId, Guid tenantId, CancellationToken ct);
    Task UpdateDeviceLastSeenAsync(Guid deviceRegistrationId, DateTimeOffset lastSeenAt, CancellationToken ct);
    Task DeactivateDeviceAsync(Guid deviceRegistrationId, DateTimeOffset deactivatedAt, CancellationToken ct);

    /// <summary>Undoes DeactivateDeviceAsync (an undone device-change approval). The revoked refresh tokens stay
    /// revoked: the old device signs in again.</summary>
    Task ReactivateDeviceAsync(Guid deviceRegistrationId, CancellationToken ct);

    Task<TrayDeviceRegistration?> FindDeviceAsync(Guid deviceRegistrationId, Guid tenantId, CancellationToken ct);

    /// <summary>HR profile for display purposes only — returns null if the user has no linked Employee row yet.</summary>
    Task<TrayEmployeeProfile?> FindEmployeeProfileAsync(
        Guid userId, Guid tenantId, Guid? legalEntityId, CancellationToken ct);
}

public sealed record TrayEmployeeProfile(
    string FirstName,
    string LastName,
    string Email,
    string EmployeeNumber,
    string? DepartmentName = null,
    string? WorkModeLabel = null,
    string? OfficeName = null,
    string? OrganizationName = null);
