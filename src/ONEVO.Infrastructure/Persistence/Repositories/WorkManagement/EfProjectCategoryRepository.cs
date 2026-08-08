using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfProjectCategoryRepository : IProjectCategoryRepository
{
    private readonly ApplicationDbContext _db;

    public EfProjectCategoryRepository(ApplicationDbContext db) => _db = db;

    public async Task<ProjectCategory?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.ProjectCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, ct);
    }

    public async Task<IReadOnlyList<ProjectCategory>> GetAllForTenantAsync(Guid tenantId, bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _db.ProjectCategories.AsNoTracking().Where(c => c.TenantId == tenantId);
        if (!includeInactive)
            query = query.Where(c => c.IsActive);

        return await query.OrderBy(c => c.Name).ToListAsync(ct);
    }
}
