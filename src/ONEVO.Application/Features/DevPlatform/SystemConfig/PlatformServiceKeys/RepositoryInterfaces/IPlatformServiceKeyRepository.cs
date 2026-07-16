using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.RepositoryInterfaces;

/// <summary>
/// Data access contract for platform service key management.
/// Phase 1 canonical table: platform_service_keys.
/// SECURITY: callers must encrypt api key material via IEncryptionService before save;
/// this repository never decrypts or logs anything.
/// </summary>
public interface IPlatformServiceKeyRepository
{
    Task<IReadOnlyList<PlatformServiceKey>> ListAllAsync(CancellationToken ct);

    /// <summary>Tracked lookup by service_key slug. Returns null when unknown.</summary>
    Task<PlatformServiceKey?> GetByServiceKeyAsync(string serviceKey, CancellationToken ct);

    Task AddAsync(PlatformServiceKey key, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
