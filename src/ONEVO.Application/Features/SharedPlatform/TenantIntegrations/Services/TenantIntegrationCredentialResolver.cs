using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Services;

public sealed class TenantIntegrationCredentialResolver : ITenantIntegrationCredentialResolver
{
    private readonly ITenantIntegrationCredentialRepository _repository;
    private readonly IEncryptionService _encryption;

    public TenantIntegrationCredentialResolver(
        ITenantIntegrationCredentialRepository repository,
        IEncryptionService encryption)
    {
        _repository = repository;
        _encryption = encryption;
    }

    public async Task<ResolvedTenantIntegrationCredential?> GetConnectedCredentialAsync(
        Guid tenantId, string integrationKey, CancellationToken ct)
    {
        var normalizedIntegrationKey = TenantIntegrationCredentialRules.NormalizeIntegrationKey(
            integrationKey);
        var credential = await _repository.GetByTenantAndIntegrationAsync(
            tenantId, normalizedIntegrationKey, ct);
        if (credential is null || credential.Status != "connected")
        {
            return null;
        }

        var accessToken = credential.AccessTokenEncrypted is null
            ? null : _encryption.Decrypt(credential.AccessTokenEncrypted);
        var refreshToken = credential.RefreshTokenEncrypted is null
            ? null : _encryption.Decrypt(credential.RefreshTokenEncrypted);

        return new ResolvedTenantIntegrationCredential(
            credential.TenantId, credential.IntegrationKey, accessToken, refreshToken,
            credential.TokenExpiresAt, credential.ScopesGranted,
            credential.ExternalAccountId, credential.ExternalAccountName);
    }
}
