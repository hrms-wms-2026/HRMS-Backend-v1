using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfObjectiveRepository : IObjectiveRepository
{
    private readonly ApplicationDbContext _db;

    public EfObjectiveRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(Objective objective, CancellationToken ct = default)
    {
        await _db.Objectives.AddAsync(objective, ct);
    }

    public async Task<Objective?> GetDefaultByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
    {
        return await _db.Objectives
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.ProjectId == projectId && o.IsDefault, ct);
    }

    public async Task<Objective?> GetTrackedDefaultByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
    {
        // Deliberately no AsNoTracking - see interface doc. Callers must mutate and then call
        // SaveChanges without an explicit Update(), so only actually-changed columns are written.
        return await _db.Objectives
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.ProjectId == projectId && o.IsDefault, ct);
    }

    public async Task<Objective?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.Objectives
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == id, ct);
    }

    public async Task<IReadOnlyList<Objective>> GetTreeByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
    {
        return await _db.Objectives
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.ProjectId == projectId && o.IsActive)
            .ToListAsync(ct);
    }

    public void Update(Objective objective)
    {
        _db.Objectives.Update(objective);
    }
}
