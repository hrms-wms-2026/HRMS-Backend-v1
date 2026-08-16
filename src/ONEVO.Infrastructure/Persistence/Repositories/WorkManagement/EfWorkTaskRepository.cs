using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public class EfWorkTaskRepository : IWorkTaskRepository
{
    private readonly ApplicationDbContext _db;

    public EfWorkTaskRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(WorkTask task, CancellationToken ct = default)
        => await _db.WorkTasks.AddAsync(task, ct);

    public async Task<WorkTask?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.WorkTasks.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, ct);

    public async Task<WorkTask?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.WorkTasks.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == id, ct);

    public async Task<IReadOnlyList<WorkTask>> GetByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default)
        => await _db.WorkTasks.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.ObjectiveId == objectiveId)
            .ToListAsync(ct);

    public async Task<decimal> GetActiveAllocationSumByObjectiveIdAsync(Guid tenantId, Guid objectiveId, Guid? excludingTaskId = null, CancellationToken ct = default)
        => await _db.WorkTasks.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.ObjectiveId == objectiveId && t.Id != (excludingTaskId ?? Guid.Empty))
            .SumAsync(t => t.EstimatedHours ?? 0m, ct);

    public void Update(WorkTask task) => _db.WorkTasks.Update(task);
}
