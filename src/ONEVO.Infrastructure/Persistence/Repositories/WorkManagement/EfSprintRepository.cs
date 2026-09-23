using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfSprintRepository : ISprintRepository
{
    private readonly ApplicationDbContext _db;

    public EfSprintRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(Sprint sprint, CancellationToken ct = default)
        => await _db.Sprints.AddAsync(sprint, ct);

    public async Task<Sprint?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.Sprints.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, ct);

    public async Task<Sprint?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.Sprints.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, ct);

    public async Task<IReadOnlyList<Sprint>> GetByProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
        => await _db.Sprints.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.ProjectId == projectId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Sprint>> GetContainingObjectiveTasksAsync(Guid tenantId, Guid objectiveId, bool activeOnly, CancellationToken ct = default)
    {
        var sprintIds = _db.WorkTasks
            .Where(t => t.TenantId == tenantId && t.ObjectiveId == objectiveId && t.SprintId != null)
            .Select(t => t.SprintId!.Value);
        return await _db.Sprints.AsNoTracking()
            .Where(s => s.TenantId == tenantId && sprintIds.Contains(s.Id)
                        && (!activeOnly || s.Status == SprintStatuses.Active))
            .ToListAsync(ct);
    }

    public async Task<bool> AnyActiveContainingObjectiveTasksAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default)
        => await _db.WorkTasks.AnyAsync(t => t.TenantId == tenantId && t.ObjectiveId == objectiveId && t.SprintId != null
               && _db.Sprints.Any(s => s.Id == t.SprintId && s.Status == SprintStatuses.Active), ct);

    public async Task<IReadOnlyList<Sprint>> GetByStatusAsync(string status, CancellationToken ct = default)
        => await _db.Sprints.Where(s => s.Status == status).ToListAsync(ct);

    public void Update(Sprint sprint) => _db.Sprints.Update(sprint);
}
