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
}
