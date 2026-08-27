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

    public async Task<IReadOnlyList<WorkTask>> GetAssignedToEmployeeWithinRangeAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        return await _db.WorkTasks.AsNoTracking()
            .Where(t => t.TenantId == tenantId
                        && t.DueDate.HasValue
                        && t.DueDate >= from
                        && t.DueDate <= to
                        && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MyTaskRow>> GetMyActiveTasksAsync(Guid tenantId, Guid employeeId, DateOnly upcomingCutoff, CancellationToken ct = default)
    {
        return await (
            from t in _db.WorkTasks.AsNoTracking()
            join s in _db.TaskStatuses.AsNoTracking() on t.StatusId equals s.Id
            join p in _db.Projects.AsNoTracking() on t.ProjectId equals p.Id
            where t.TenantId == tenantId
                  && t.DueDate.HasValue
                  && t.DueDate <= upcomingCutoff
                  && !s.MarksTaskComplete
                  && _db.TaskAssignments.Any(a => a.TaskId == t.Id && a.EmployeeId == employeeId)
            orderby t.DueDate
            select new MyTaskRow(t.Id, t.ShortId, t.Title, t.DueDate!.Value, t.ProjectId, p.Name, t.ObjectiveId, t.Priority)
        ).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<WorkTask>> GetBySprintIdAsync(Guid tenantId, Guid sprintId, CancellationToken ct = default)
        => await _db.WorkTasks.AsNoTracking().Where(t => t.TenantId == tenantId && t.SprintId == sprintId).ToListAsync(ct);

    // IgnoreQueryFilters() bypasses the soft-delete half of the composed query filter on
    // purpose: a status must stay undeletable if a soft-deleted task still references it, not
    // just active ones. Tenant scoping is preserved manually via the TenantId equality below.
    public async Task<bool> AnyActiveByStatusIdAsync(Guid tenantId, Guid statusId, CancellationToken ct = default)
        => await _db.WorkTasks.IgnoreQueryFilters()
            .AnyAsync(t => t.TenantId == tenantId && t.StatusId == statusId, ct);

    // IgnoreQueryFilters() bypasses the soft-delete half of the composed query filter on
    // purpose: a category must stay undeletable if a soft-deleted task still references it, not
    // just active ones. Tenant scoping is preserved manually via the TenantId equality below.
    public async Task<bool> AnyActiveByCategoryIdAsync(Guid tenantId, Guid categoryId, CancellationToken ct = default)
        => await _db.WorkTasks.IgnoreQueryFilters()
            .AnyAsync(t => t.TenantId == tenantId && t.CategoryId == categoryId, ct);

    public void Update(WorkTask task) => _db.WorkTasks.Update(task);
    public void Remove(WorkTask task) => _db.WorkTasks.Remove(task);
}
