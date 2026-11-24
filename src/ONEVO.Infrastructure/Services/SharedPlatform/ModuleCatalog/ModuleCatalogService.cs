using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Caching;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.SharedPlatform;

public sealed class ModuleCatalogService : IModuleCatalogService
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantCache _cache;

    public ModuleCatalogService(ApplicationDbContext db, ITenantCache? cache = null)
    {
        _db = db;
        _cache = cache ?? PassThroughTenantCache.Instance;
    }

    public async Task<IReadOnlySet<string>> GetActiveModuleKeysAsync(CancellationToken ct = default)
    {
        var keys = await _cache.GetOrCreateGlobalAsync(
            ReferenceDataCacheKeys.ActiveModuleCatalog,
            async token => (await _db.ModuleCatalog
                .AsNoTracking()
                .Where(m => m.IsActive)
                .Select(m => m.ModuleKey)
                .ToListAsync(token)).ToArray(),
            ct);

        return keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<ModuleCatalogItem>> GetByCatalogKeysAsync(
        IReadOnlyList<string> keys,
        CancellationToken ct = default)
    {
        return await _db.ModuleCatalog
            .AsNoTracking()
            .Where(m => keys.Contains(m.ModuleKey))
            .ToListAsync(ct);
    }
}
