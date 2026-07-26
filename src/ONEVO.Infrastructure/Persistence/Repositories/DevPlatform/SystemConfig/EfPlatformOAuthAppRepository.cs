using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig;

/// <summary>
/// EF Core repository for ONEVO OAuth app registrations and credential versions.
/// Phase 1 canonical tables: platform_oauth_apps, platform_oauth_app_credentials.
/// SECURITY: secret material reaches here already encrypted (IEncryptionService);
/// this repository never decrypts or logs anything.
/// </summary>
public sealed class EfPlatformOAuthAppRepository : IPlatformOAuthAppRepository
{
    private readonly ApplicationDbContext _db;

    public EfPlatformOAuthAppRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PlatformOAuthApp>> ListAllAsync(CancellationToken ct)
    {
        var query = _db.PlatformOAuthApps
            .AsNoTracking()
            .OrderBy(app => app.Provider);

        var apps = await query.ToListAsync(ct);

        return apps;
    }

    public async Task<PlatformOAuthApp?> GetByProviderAsync(
        string provider,
        CancellationToken ct)
    {
        var query = _db.PlatformOAuthApps
            .Where(app => app.Provider == provider);

        var app = await query.FirstOrDefaultAsync(ct);

        return app;
    }

    public async Task<IReadOnlyList<PlatformOAuthAppCredential>> GetActiveCredentialsForAppAsync(
        Guid platformOAuthAppId,
        CancellationToken ct)
    {
        var query = _db.PlatformOAuthAppCredentials
            .Where(credential => credential.PlatformOAuthAppId == platformOAuthAppId)
            .Where(credential => credential.IsActive);

        var credentials = await query.ToListAsync(ct);

        return credentials;
    }

    public async Task<IReadOnlyList<PlatformOAuthAppCredential>> ListActiveCredentialsAsync(
        CancellationToken ct)
    {
        var query = _db.PlatformOAuthAppCredentials
            .AsNoTracking()
            .Where(credential => credential.IsActive);

        var credentials = await query.ToListAsync(ct);

        return credentials;
    }

    public async Task<int> GetMaxCredentialVersionAsync(
        Guid platformOAuthAppId,
        CancellationToken ct)
    {
        var query = _db.PlatformOAuthAppCredentials
            .Where(credential => credential.PlatformOAuthAppId == platformOAuthAppId)
            .Select(credential => credential.CredentialVersion);

        var credentialVersions = await query.ToListAsync(ct);

        return credentialVersions.Count == 0
            ? 0
            : credentialVersions.Max();
    }

    public async Task AddAsync(PlatformOAuthApp app, CancellationToken ct)
    {
        await _db.PlatformOAuthApps.AddAsync(app, ct);
    }

    public async Task AddCredentialAsync(PlatformOAuthAppCredential credential, CancellationToken ct)
    {
        await _db.PlatformOAuthAppCredentials.AddAsync(credential, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await _db.SaveChangesAsync(ct);
    }
}
