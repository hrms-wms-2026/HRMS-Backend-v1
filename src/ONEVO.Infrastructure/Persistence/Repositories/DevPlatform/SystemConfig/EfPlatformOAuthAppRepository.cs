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

    public EfPlatformOAuthAppRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<PlatformOAuthApp>> ListAllAsync(CancellationToken ct)
        => await _db.PlatformOAuthApps
            .AsNoTracking()
            .OrderBy(a => a.Provider)
            .ToListAsync(ct);

    public Task<PlatformOAuthApp?> GetByProviderAsync(string provider, CancellationToken ct)
        => _db.PlatformOAuthApps.FirstOrDefaultAsync(a => a.Provider == provider, ct);

    public async Task<IReadOnlyList<PlatformOAuthAppCredential>> GetActiveCredentialsForAppAsync(
        Guid platformOAuthAppId, CancellationToken ct)
        => await _db.PlatformOAuthAppCredentials
            .Where(c => c.PlatformOAuthAppId == platformOAuthAppId && c.IsActive)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PlatformOAuthAppCredential>> ListActiveCredentialsAsync(CancellationToken ct)
        => await _db.PlatformOAuthAppCredentials
            .AsNoTracking()
            .Where(c => c.IsActive)
            .ToListAsync(ct);

    public async Task<int> GetMaxCredentialVersionAsync(Guid platformOAuthAppId, CancellationToken ct)
    {
        var versions = await _db.PlatformOAuthAppCredentials
            .Where(c => c.PlatformOAuthAppId == platformOAuthAppId)
            .Select(c => c.CredentialVersion)
            .ToListAsync(ct);

        return versions.Count == 0 ? 0 : versions.Max();
    }

    public async Task AddAsync(PlatformOAuthApp app, CancellationToken ct)
        => await _db.PlatformOAuthApps.AddAsync(app, ct);

    public async Task AddCredentialAsync(PlatformOAuthAppCredential credential, CancellationToken ct)
        => await _db.PlatformOAuthAppCredentials.AddAsync(credential, ct);

    public Task SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);
}
