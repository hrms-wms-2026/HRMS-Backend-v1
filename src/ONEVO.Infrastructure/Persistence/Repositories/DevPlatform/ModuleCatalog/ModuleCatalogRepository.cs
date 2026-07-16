using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.ModuleCatalog.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.ModuleCatalog;

public class ModuleCatalogRepository : IModuleCatalogRepository
{
    private readonly ApplicationDbContext _db;

    public ModuleCatalogRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ModuleCatalogItem>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.ModuleCatalog
            .AsNoTracking()
            .OrderBy(m => m.Phase).ThenBy(m => m.ModuleKey)
            .ToListAsync(ct);
    }

    public async Task<ModuleCatalogItem?> GetByKeyAsync(string moduleKey, CancellationToken ct = default)
    {
        return await _db.ModuleCatalog
            .Include(m => m.Features)
            .Include(m => m.PermissionOwnerships)
            .Include(m => m.PriceHistories)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ModuleKey == moduleKey, ct);
    }
}
