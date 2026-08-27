using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface IWorkTaskRepository
{
    Task AddAsync(WorkTask task, CancellationToken ct = default);
    Task<WorkTask?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<WorkTask?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WorkTask>> GetByObjectiveIdAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkTask>> GetByProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    /// <summary>SUM(EstimatedHours) across active tasks in this Objective — the "SUM(direct_tasks.estimated_hours)"
    /// half of the slack formula in spec §3.1. Excludes the task identified by `excludingTaskId` (used on
    /// edit, to avoid double-counting the task's own current value against its own proposed new value).</summary>
    Task<decimal> GetActiveAllocationSumByObjectiveIdAsync(Guid tenantId, Guid objectiveId, Guid? excludingTaskId = null, CancellationToken ct = default);

    /// <summary>Tasks with an assignment to this employee and DueDate in [from, to]. For the
    /// my-deadlines endpoint (spec §7) - not used by any other query.</summary>
    Task<IReadOnlyList<WorkTask>> GetAssignedToEmployeeWithinRangeAsync(Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<IReadOnlyList<WorkTask>> GetBySprintIdAsync(Guid tenantId, Guid sprintId, CancellationToken ct = default);

    /// <summary>True if any physical WorkTask row, including a soft-deleted row, has this StatusId
    /// within the tenant - used to block deleting a status while a restricted FK still references it.</summary>
    Task<bool> AnyActiveByStatusIdAsync(Guid tenantId, Guid statusId, CancellationToken ct = default);

    /// <summary>True if any physical WorkTask row, including a soft-deleted row, has this CategoryId
    /// within the tenant - used to block deleting a category while a restricted FK still references it.</summary>
    Task<bool> AnyActiveByCategoryIdAsync(Guid tenantId, Guid categoryId, CancellationToken ct = default);

    void Update(WorkTask task);
    void Remove(WorkTask task);
}
