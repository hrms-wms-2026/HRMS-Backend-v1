using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.SystemConfig;

/// <summary>
/// Server-side resolver for ONEVO OAuth app registrations (future tenant/user
/// OAuth connect flows). Decrypts secret material in memory for server-side
/// callers only.
/// SECURITY: resolved values must never reach a controller response or a log.
/// </summary>
public sealed class PlatformOAuthAppResolver : IPlatformOAuthAppResolver
{
    private readonly IPlatformOAuthAppRepository _repo;
    private readonly IEncryptionService _encryption;

    public PlatformOAuthAppResolver(
        IPlatformOAuthAppRepository repo,
        IEncryptionService encryption)
    {
        _repo = repo;
        _encryption = encryption;
    }

    public async Task<ResolvedPlatformOAuthApp?> GetActiveAppForProviderAsync(
        string provider, CancellationToken ct)
    {
        var app = await _repo.GetByProviderAsync(
            PlatformOAuthProviderRules.Normalize(provider), ct);
        if (app is null || !app.IsActive)
            return null;

        return new ResolvedPlatformOAuthApp(
            app.Provider,
            app.ClientId,
            app.AuthorizationUrl,
            app.TokenUrl,
            app.DefaultScopes);
    }

    public async Task<ResolvedPlatformOAuthAppCredential?> GetActiveCredentialForProviderAsync(
        string provider, CancellationToken ct)
    {
        var app = await _repo.GetByProviderAsync(
            PlatformOAuthProviderRules.Normalize(provider), ct);
        if (app is null || !app.IsActive)
            return null;

        var activeCredentials = await _repo.GetActiveCredentialsForAppAsync(app.Id, ct);
        var credential = activeCredentials.FirstOrDefault();
        if (credential is null)
            return null;

        return new ResolvedPlatformOAuthAppCredential(
            app.Provider,
            app.ClientId,
            _encryption.Decrypt(credential.ClientSecretEncrypted),
            credential.PrivateKeyEncrypted is not null
                ? _encryption.Decrypt(credential.PrivateKeyEncrypted)
                : null,
            credential.CredentialVersion);
    }
}
