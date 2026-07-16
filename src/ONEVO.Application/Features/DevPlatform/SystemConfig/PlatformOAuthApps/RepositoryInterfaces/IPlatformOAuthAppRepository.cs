using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;

/// <summary>
/// Data access contract for ONEVO OAuth app registrations and their credential versions.
/// Phase 1 canonical tables: platform_oauth_apps, platform_oauth_app_credentials.
/// SECURITY: callers must encrypt secret material via IEncryptionService before save;
/// this repository never decrypts or logs anything.
/// </summary>
public interface IPlatformOAuthAppRepository
{
    Task<IReadOnlyList<PlatformOAuthApp>> ListAllAsync(CancellationToken ct);

    /// <summary>Tracked lookup by lowercase provider slug. Returns null when unknown.</summary>
    Task<PlatformOAuthApp?> GetByProviderAsync(string provider, CancellationToken ct);

    /// <summary>Tracked active credential rows for one app (business rule: at most one).</summary>
    Task<IReadOnlyList<PlatformOAuthAppCredential>> GetActiveCredentialsForAppAsync(
        Guid platformOAuthAppId, CancellationToken ct);

    /// <summary>Read-only active credential rows across all apps (list-screen enrichment).</summary>
    Task<IReadOnlyList<PlatformOAuthAppCredential>> ListActiveCredentialsAsync(CancellationToken ct);

    /// <summary>Highest credential_version ever issued for the app; 0 when none exist.</summary>
    Task<int> GetMaxCredentialVersionAsync(Guid platformOAuthAppId, CancellationToken ct);

    Task AddAsync(PlatformOAuthApp app, CancellationToken ct);

    Task AddCredentialAsync(PlatformOAuthAppCredential credential, CancellationToken ct);

    /// <summary>Single SaveChanges = one atomic transaction for create/rotate flows.</summary>
    Task SaveChangesAsync(CancellationToken ct);
}
