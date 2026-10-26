using ONEVO.Domain.Features.WorkManagement.Planner.Entities;

namespace ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;

/// <param name="AssignedToEmployee">The task is currently assigned to the employee the query was for.</param>
public sealed record PlannerTaskInfo(Guid TaskId, string Title, DateOnly? DueDate, decimal? EstimatedHours, bool IsMeeting,
    bool AssignedToEmployee);

public sealed record PlannerSession(Guid TaskId, DateTimeOffset ClockInAt, DateTimeOffset? ClockOutAt);

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

    /// <summary>The employee's task clocking sessions overlapping [from, to).</summary>
    Task<IReadOnlyList<PlannerSession>> ListSessionsAsync(Guid tenantId, Guid employeeId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);

    /// <summary>The employee's attendance record for that date, if any.</summary>
    Task<PlannerAttendance?> GetAttendanceAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct = default);

    void AddBlock(PlannedTaskBlock block);
    void RemoveBlock(PlannedTaskBlock block);
}
