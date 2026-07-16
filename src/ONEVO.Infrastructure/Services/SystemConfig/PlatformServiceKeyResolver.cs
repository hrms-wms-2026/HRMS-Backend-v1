using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.SystemConfig;

/// <summary>
/// Runtime resolver for ONEVO-owned platform service credentials.
/// Decrypts in memory for server-side callers only (future email/outbox sending).
/// SECURITY: the decrypted value must never reach a controller response or a log.
/// </summary>
public sealed class PlatformServiceKeyResolver : IPlatformServiceKeyResolver
{
    private readonly IPlatformServiceKeyRepository _repo;
    private readonly IEncryptionService _encryption;

    public PlatformServiceKeyResolver(
        IPlatformServiceKeyRepository repo,
        IEncryptionService encryption)
    {
        _repo = repo;
        _encryption = encryption;
    }

    public async Task<string?> ResolveActiveKeyAsync(string serviceKey, CancellationToken ct)
    {
        var entity = await _repo.GetByServiceKeyAsync(serviceKey, ct);
        if (entity is null || !entity.IsActive)
            return null;

        return _encryption.Decrypt(entity.ApiKeyEncrypted);
    }
}
