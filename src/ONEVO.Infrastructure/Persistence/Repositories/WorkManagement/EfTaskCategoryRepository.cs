using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.Caching;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Services.SharedPlatform;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskCategoryRepository : ITaskCategoryRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantCache _cache;

    public EfTaskCategoryRepository(ApplicationDbContext db, ITenantCache? cache = null)
    {
        _db = db;
        _cache = cache ?? PassThroughTenantCache.Instance;
    }

    public async Task AddAsync(TaskCategory category, CancellationToken ct = default)
        => await _db.TaskCategories.AddAsync(category, ct);

    public async Task AddRangeAsync(IReadOnlyList<TaskCategory> categories, CancellationToken ct = default)
        => await _db.TaskCategories.AddRangeAsync(categories, ct);

    /// <summary>Cached per tenant and project (task 22); every caller gets its own copies (the reorder handler edits them).</summary>
    public async Task<IReadOnlyList<TaskCategory>> GetByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
    {
        var categories = await _cache.GetOrCreateAsync<IReadOnlyList<TaskCategory>>(
            tenantId, ReferenceDataCacheKeys.TaskCategories, projectId.ToString("N"),
            async token => await _db.TaskCategories.AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.ProjectId == projectId)
                .OrderBy(c => c.DisplayOrder)
                .ToListAsync(token),
            ct);
        return ReferenceDataCopy.All(categories);
    }

    public async Task<TaskCategory?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.TaskCategories.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, ct);

    public void Update(TaskCategory category) => _db.TaskCategories.Update(category);
    public void Remove(TaskCategory category) => _db.TaskCategories.Remove(category);
}
