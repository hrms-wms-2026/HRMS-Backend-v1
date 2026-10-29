using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.WorkManagement.Planner.Entities;

namespace ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;

/// <param name="AssignedToEmployee">The task is currently assigned to the employee the query was for.</param>
public sealed record PlannerTaskInfo(Guid TaskId, string Title, DateOnly? DueDate, decimal? EstimatedHours, bool IsMeeting,
    bool AssignedToEmployee, Guid ProjectId, string ProjectName = "", string CategoryName = "", string CategoryKind = "task");

public sealed record PlannerSession(Guid TaskId, DateTimeOffset ClockInAt, DateTimeOffset? ClockOutAt);

/// <summary>An open task assigned to the employee, for the Focus list.</summary>
/// <param name="LoggedMinutes">Every non-deleted clocking session on the task, by anyone; an open one up to now.</param>
public sealed record PlannerFocusTaskRow(Guid TaskId, Guid ProjectId, string ProjectName, string Title, string Priority, DateOnly? DueDate,
    decimal? EstimatedHours, int LoggedMinutes, string CategoryName = "", string CategoryKind = "task",
    DateTimeOffset CreatedAt = default);

public sealed record PlannerAttendance(DateTimeOffset? ActualStart, DateTimeOffset? ActualEnd, int WorkedMinutes, int BreakMinutes);

/// <summary>Day planner reads and writes (2026-10-26 design, Part B). Never calls SaveChangesAsync.</summary>
public interface IPlannerRepository
{
    Task<IReadOnlyList<PlannedTaskBlock>> ListBlocksAsync(Guid tenantId, Guid employeeId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);

    Task<PlannedTaskBlock?> GetBlockTrackedAsync(Guid tenantId, Guid employeeId, Guid blockId, CancellationToken ct = default);

    /// <summary>Whether another of the employee's blocks overlaps [start, end).</summary>
    Task<bool> OverlapsAsync(Guid tenantId, Guid employeeId, DateTimeOffset start, DateTimeOffset end, Guid? exceptBlockId,
        CancellationToken ct = default);

    /// <summary>The given tasks (meetings included, deleted ones excluded), each flagged with whether it is assigned to the employee.</summary>
    Task<IReadOnlyDictionary<Guid, PlannerTaskInfo>> GetTasksAsync(Guid tenantId, Guid employeeId, IReadOnlyCollection<Guid> taskIds,
        CancellationToken ct = default);

    /// <summary>Tasks assigned to the employee, not done, not meetings, due on or before <paramref name="date"/>.</summary>
    Task<IReadOnlyList<PlannerTaskInfo>> ListDueAssignedTasksAsync(Guid tenantId, Guid employeeId, DateOnly date,
        CancellationToken ct = default);

    /// <summary>Every task assigned to the employee that is not done, not deleted and not a meeting, with its project and
    /// everyone's logged minutes up to <paramref name="now"/>.</summary>
    Task<IReadOnlyList<PlannerFocusTaskRow>> ListFocusTasksAsync(Guid tenantId, Guid employeeId, DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>The employee's task clocking sessions overlapping [from, to).</summary>
    Task<IReadOnlyList<PlannerSession>> ListSessionsAsync(Guid tenantId, Guid employeeId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);

    /// <summary>The employee's own legal entity, else the tenant's primary one, else null (for the work window).</summary>
    Task<LegalEntity?> GetEmployeeLegalEntityAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    /// <summary>The employee's attendance record for that date, if any.</summary>
    Task<PlannerAttendance?> GetAttendanceAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct = default);

    /// <summary>Takes the employee's plan lock for the rest of the caller's transaction, so one write's overlap check and
    /// insert never interleave with another's (two devices saving at once).</summary>
    Task LockPlanAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    void AddBlock(PlannedTaskBlock block);
    void RemoveBlock(PlannedTaskBlock block);
}
