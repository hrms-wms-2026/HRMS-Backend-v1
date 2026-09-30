using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
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

    public async Task<IReadOnlyList<Objective>> GetByIdsForTenantAsync(
        Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return Array.Empty<Objective>();

        return await _db.Objectives
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId && ids.Contains(o.Id))
            .ToListAsync(ct);
    }

    public async Task<Objective?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.Objectives
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == id, ct);
    }

    public async Task<IReadOnlyList<Objective>> GetTreeByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
    {
        return await _db.Objectives
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.ProjectId == projectId && o.IsActive)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Objective>> GetAllByProjectIdAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
    {
        return await _db.Objectives
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.ProjectId == projectId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Objective>> GetTrackedActiveDirectChildrenAsync(Guid tenantId, Guid parentObjectiveId, CancellationToken ct = default)
    {
        return await _db.Objectives
            .Where(o => o.TenantId == tenantId && o.ParentObjectiveId == parentObjectiveId && o.IsActive)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Objective>> GetOwnedByEmployeeIdWithinRangeAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => await _db.Objectives.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.OwnerId == employeeId && o.IsActive && o.EndDate >= from && o.EndDate <= to)
            .ToListAsync(ct);

    public async Task<bool> AnyActiveOwnedAsync(Guid tenantId, Guid ownerEmployeeId, Guid legalEntityId, CancellationToken ct = default)
        => await OwnedActive(tenantId, ownerEmployeeId, legalEntityId).AnyAsync(ct);

    public async Task<IReadOnlyList<(Guid ObjectiveId, Guid ProjectId)>> ListActiveOwnedIdsAsync(Guid tenantId, Guid ownerEmployeeId, Guid legalEntityId, CancellationToken ct = default)
        => (await OwnedActive(tenantId, ownerEmployeeId, legalEntityId).Select(o => new { o.Id, o.ProjectId }).ToListAsync(ct))
            .Select(x => (x.Id, x.ProjectId)).ToList();

    private IQueryable<Objective> OwnedActive(Guid tenantId, Guid ownerEmployeeId, Guid legalEntityId) =>
        from o in _db.Objectives.AsNoTracking()
        join p in _db.Projects.AsNoTracking() on o.ProjectId equals p.Id
        where o.TenantId == tenantId && p.TenantId == tenantId
            && o.OwnerId == ownerEmployeeId && o.IsActive && !o.IsAchieved
            && p.IsActive && p.OwningLegalEntityId == legalEntityId
        select o;

    public async Task<IReadOnlyList<LedObjectiveRow>> ListActiveTreeForProjectsAsync(Guid tenantId, IReadOnlyCollection<Guid> projectIds, CancellationToken ct = default)
    {
        if (projectIds.Count == 0)
            return Array.Empty<LedObjectiveRow>();
        var ids = projectIds.ToList();
        return await _db.Objectives.AsNoTracking()
            .Where(o => o.TenantId == tenantId && ids.Contains(o.ProjectId) && o.IsActive)
            .Select(o => new LedObjectiveRow(o.Id, o.ProjectId, o.ParentObjectiveId, o.Title, o.IsDefault, o.EndDate))
            .ToListAsync(ct);
    }

    public void Update(Objective objective)
    {
        _db.Objectives.Update(objective);
    }
}
