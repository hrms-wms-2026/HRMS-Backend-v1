using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface IWorkTaskRepository
{
    Task AddAsync(WorkTask task, CancellationToken ct = default);
    Task<WorkTask?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<WorkTask?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WorkTask>> GetByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);

    /// <summary>SUM(EstimatedHours) across active tasks in this Objective — the "SUM(direct_tasks.estimated_hours)"
    /// half of the slack formula in spec §3.1. Excludes the task identified by `excludingTaskId` (used on
    /// edit, to avoid double-counting the task's own current value against its own proposed new value).</summary>
    Task<decimal> GetActiveAllocationSumByObjectiveIdAsync(Guid tenantId, Guid objectiveId, Guid? excludingTaskId = null, CancellationToken ct = default);

    void Update(WorkTask task);
}
