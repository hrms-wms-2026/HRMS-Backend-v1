using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;

public interface IPlatformUserCredentialRepository
{
    Task<PlatformUserCredential?> GetActivePasswordCredentialAsync(
        Guid platformUserId,
        CancellationToken ct = default);

    Task AddAsync(PlatformUserCredential credential, CancellationToken ct = default);
    void Update(PlatformUserCredential credential);

    /// <summary>
    /// Atomically claims a live (unexpired, not-yet-consumed) reset token: a single
    /// UPDATE ... WHERE reset_token_expires_at > now guard clears the expiry so a second
    /// concurrent caller can never also win. Returns the owning PlatformUserId on success,
    /// null if the token was unknown, expired, or already consumed.
    /// </summary>
    Task<Guid?> TryConsumeResetTokenAsync(
        string tokenHash, DateTimeOffset now, CancellationToken ct = default);
}
