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
    Task RevokeRefreshTokenAsync(TrayDeviceRefreshToken token, string reason, CancellationToken ct);
    Task RevokeAllRefreshTokensForDeviceAsync(Guid deviceRegistrationId, string reason, CancellationToken ct);

    Task<TrayDeviceRegistration?> FindActiveDeviceAsync(Guid deviceRegistrationId, Guid tenantId, CancellationToken ct);
    Task<TrayDeviceRegistration?> FindLatestActiveDeviceForUserAsync(Guid userId, Guid tenantId, CancellationToken ct);
    Task UpdateDeviceLastSeenAsync(Guid deviceRegistrationId, DateTimeOffset lastSeenAt, CancellationToken ct);
    Task DeactivateDeviceAsync(Guid deviceRegistrationId, DateTimeOffset deactivatedAt, CancellationToken ct);

    /// <summary>HR profile for display purposes only — returns null if the user has no linked Employee row yet.</summary>
    Task<TrayEmployeeProfile?> FindEmployeeProfileAsync(
        Guid userId, Guid tenantId, Guid? legalEntityId, CancellationToken ct);
}

public sealed record TrayEmployeeProfile(string FirstName, string LastName, string Email, string EmployeeNumber);
