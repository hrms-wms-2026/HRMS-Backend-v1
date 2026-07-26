using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformProviders.Entities;

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

    public async Task<TransactionalEmailProviderResolution> ResolveActiveTransactionalEmailProviderAsync(
        CancellationToken ct)
    {
        var matches = await _repo.ListActiveForProviderFamilyAsync(
            PlatformProviderFamilies.TransactionalEmail,
            ct);

        if (matches.Count == 0)
            return TransactionalEmailProviderResolution.NotConfigured();

        if (matches.Count > 1)
            return TransactionalEmailProviderResolution.Ambiguous();

        var match = matches[0];
        var apiKey = _encryption.Decrypt(match.ApiKeyEncrypted);
        return TransactionalEmailProviderResolution.Resolved(match.ServiceKey, apiKey);
    }
}
