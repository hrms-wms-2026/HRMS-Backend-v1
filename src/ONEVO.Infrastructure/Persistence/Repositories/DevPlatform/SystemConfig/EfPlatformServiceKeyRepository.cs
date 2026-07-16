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

    public EfPlatformServiceKeyRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<PlatformServiceKey>> ListAllAsync(CancellationToken ct)
        => await _db.PlatformServiceKeys
            .AsNoTracking()
            .OrderBy(k => k.ServiceKey)
            .ToListAsync(ct);

    public Task<PlatformServiceKey?> GetByServiceKeyAsync(string serviceKey, CancellationToken ct)
        => _db.PlatformServiceKeys.FirstOrDefaultAsync(k => k.ServiceKey == serviceKey, ct);

    public async Task AddAsync(PlatformServiceKey key, CancellationToken ct)
        => await _db.PlatformServiceKeys.AddAsync(key, ct);

    public Task SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);
}
