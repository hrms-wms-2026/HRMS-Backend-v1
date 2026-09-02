using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

/// <summary>A task assigned to the caller, joined with its project name and status for the
/// My Tasks dashboard widget.</summary>
public sealed record MyTaskRow(
    Guid Id,
    string ShortId,
    string Title,
    DateOnly DueDate,
    Guid ProjectId,
    string ProjectName,
    Guid ObjectiveId,
    string Priority);

/// <summary>The bare fields needed to bucket a caller's assigned task into the Task Progress
/// donut widget's Completed/Overdue/In Progress/Not Started categories.</summary>
public sealed record TaskProgressRow(
    bool MarksTaskComplete,
    DateOnly? DueDate,
    int ProgressPercent);

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

    /// <summary>Not-yet-complete tasks assigned to this employee, due on or before
    /// upcomingCutoff - includes every overdue task regardless of age (no lower bound), plus
    /// tasks due within the upcoming window. For the My Tasks dashboard widget.</summary>
    Task<IReadOnlyList<MyTaskRow>> GetMyActiveTasksAsync(Guid tenantId, Guid employeeId, DateOnly upcomingCutoff, CancellationToken ct = default);

    /// <summary>Every not-soft-deleted task assigned to this employee, regardless of due date -
    /// for the Task Progress dashboard widget's overall completion breakdown.</summary>
    Task<IReadOnlyList<TaskProgressRow>> GetMyTaskProgressRowsAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

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
