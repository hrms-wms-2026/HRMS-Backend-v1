using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig;

/// <summary>
/// EF Core repository for platform service key management.
/// Phase 1 canonical table: platform_service_keys.
/// SECURITY: api key material reaches here already encrypted (IEncryptionService);
/// this repository never decrypts or logs anything.
/// </summary>
public sealed class EfPlatformServiceKeyRepository : IPlatformServiceKeyRepository
{
    private readonly ApplicationDbContext _db;

    public EfPlatformServiceKeyRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PlatformServiceKey>> ListAllAsync(CancellationToken ct)
    {
        var query = _db.PlatformServiceKeys
            .AsNoTracking()
            .OrderBy(k => k.ServiceKey);

        var result = await query.ToListAsync(ct);
        return result;
    }

    public async Task<PlatformServiceKey?> GetByServiceKeyAsync(string serviceKey, CancellationToken ct)
    {
        var query = _db.PlatformServiceKeys
            .Where(k => k.ServiceKey == serviceKey);

        var result = await query.FirstOrDefaultAsync(ct);
        return result;
    }

    public async Task<IReadOnlyList<PlatformServiceKey>> ListActiveForProviderFamilyAsync(
        string providerFamily,
        CancellationToken ct)
    {
        var query =
            from key in _db.PlatformServiceKeys.AsNoTracking()
            join provider in _db.PlatformProviders.AsNoTracking()
                on key.ServiceKey equals provider.ProviderKey
            where key.IsActive &&
                  provider.IsActive &&
                  provider.ProviderFamily == providerFamily
            orderby key.ServiceKey
            select key;

        return await query.ToListAsync(ct);
    }

    public async Task AddAsync(PlatformServiceKey key, CancellationToken ct)
    {
        await _db.PlatformServiceKeys.AddAsync(key, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await _db.SaveChangesAsync(ct);
    }
}
