using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfTaskCategoryRepository : ITaskCategoryRepository
{
    private readonly ApplicationDbContext _db;

    public EfTaskCategoryRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(TaskCategory category, CancellationToken ct = default)
        => await _db.TaskCategories.AddAsync(category, ct);

    public async Task AddRangeAsync(IReadOnlyList<TaskCategory> categories, CancellationToken ct = default)
        => await _db.TaskCategories.AddRangeAsync(categories, ct);

    public async Task<IReadOnlyList<TaskCategory>> GetByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
        => await _db.TaskCategories.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.ProjectId == projectId)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(ct);

    public async Task<TaskCategory?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.TaskCategories.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, ct);

    public void Update(TaskCategory category) => _db.TaskCategories.Update(category);
    public void Remove(TaskCategory category) => _db.TaskCategories.Remove(category);
}
