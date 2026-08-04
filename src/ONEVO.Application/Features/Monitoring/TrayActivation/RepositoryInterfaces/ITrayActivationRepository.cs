using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;

public interface ITrayActivationRepository
{
    Task<int> CountRecentCodesForUserAsync(Guid userId, Guid tenantId, DateTimeOffset since, CancellationToken ct);
    Task AddActivationCodeAsync(TrayActivationCode code, CancellationToken ct);

    // Exchange endpoint: tenantId unknown at call time — hash is globally unique
    Task<TrayActivationCode?> FindActiveCodeByHashAsync(string codeHash, CancellationToken ct);
    Task MarkCodeUsedAsync(TrayActivationCode code, CancellationToken ct);

    Task AddDeviceRegistrationAsync(TrayDeviceRegistration device, CancellationToken ct);
    Task AddRefreshTokenAsync(TrayDeviceRefreshToken token, CancellationToken ct);

    Task<TrayDeviceRefreshToken?> FindActiveRefreshTokenAsync(string tokenHash, CancellationToken ct);
    Task RevokeRefreshTokenAsync(TrayDeviceRefreshToken token, string reason, CancellationToken ct);
    Task RevokeAllRefreshTokensForDeviceAsync(Guid deviceRegistrationId, string reason, CancellationToken ct);

    Task<TrayDeviceRegistration?> FindActiveDeviceAsync(Guid deviceRegistrationId, Guid tenantId, CancellationToken ct);
    Task UpdateDeviceLastSeenAsync(Guid deviceRegistrationId, DateTimeOffset lastSeenAt, CancellationToken ct);
}
