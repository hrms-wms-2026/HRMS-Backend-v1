using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.SharedPlatform;

public sealed class ModuleCatalogService : IModuleCatalogService
{
    private readonly ApplicationDbContext _db;

    public ModuleCatalogService(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlySet<string>> GetActiveModuleKeysAsync(CancellationToken ct = default)
    {
        var keys = await _db.ModuleCatalog
            .AsNoTracking()
            .Where(m => m.IsActive)
            .Select(m => m.ModuleKey)
            .ToListAsync(ct);

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
