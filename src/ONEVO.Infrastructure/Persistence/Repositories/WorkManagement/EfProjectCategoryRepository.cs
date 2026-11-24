using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Caching;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Services.SharedPlatform;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfProjectCategoryRepository : IProjectCategoryRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantCache _cache;

    public EfProjectCategoryRepository(ApplicationDbContext db, ITenantCache? cache = null)
    {
        _db = db;
        _cache = cache ?? PassThroughTenantCache.Instance;
    }

    public async Task<ProjectCategory?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.ProjectCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, ct);
    }

    /// <summary>Cached per tenant (task 22); every caller gets its own copies.</summary>
    public async Task<IReadOnlyList<ProjectCategory>> GetAllForTenantAsync(Guid tenantId, bool includeInactive = false, CancellationToken ct = default)
    {
        var categories = await _cache.GetOrCreateAsync<IReadOnlyList<ProjectCategory>>(
            tenantId, ReferenceDataCacheKeys.ProjectCategories, includeInactive ? "all" : "active",
            async token =>
            {
                var query = _db.ProjectCategories.AsNoTracking().Where(c => c.TenantId == tenantId);
                if (!includeInactive)
                    query = query.Where(c => c.IsActive);
                return await query.OrderBy(c => c.Name).ToListAsync(token);
            },
            ct);
        return ReferenceDataCopy.All(categories);
    }

    public async Task<bool> ExistsByNameAsync(Guid tenantId, string name, CancellationToken ct = default)
    {
        return await _db.ProjectCategories
            .AsNoTracking()
            .AnyAsync(c => c.TenantId == tenantId && c.Name.ToLower() == name.ToLower(), ct);
    }

    public async Task AddAsync(ProjectCategory category, CancellationToken ct = default)
    {
        await _db.ProjectCategories.AddAsync(category, ct);
    }
}
