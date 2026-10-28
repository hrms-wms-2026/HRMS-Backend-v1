namespace ONEVO.Application.Features.WorkManagement.Planner.RepositoryInterfaces;

public sealed record TeamBlockRow(Guid EmployeeId, Guid TaskId, DateTimeOffset Start, DateTimeOffset End);

public sealed record TeamSessionRow(Guid EmployeeId, Guid TaskId, DateTimeOffset ClockIn, DateTimeOffset? ClockOut);

/// <param name="IsComplete">Status MarksTaskComplete or progress &gt;= 100 (the LedTaskBucket.Completed rule).</param>
public sealed record TeamTaskRow(Guid TaskId, string ShortId, string Title, Guid? ObjectiveId, Guid? ProjectId,
    bool IsComplete, decimal ProgressPercent);

/// <param name="IsActive">Employment status Active.</param>
public sealed record TeamPersonRow(Guid EmployeeId, string DisplayName, Guid? AvatarFileId, bool IsActive);

/// <summary>Batched Planner block and task clocking reads for the My Team widgets (2026-10-29 design, section 7): one
/// query per call, never per employee. "For tasks" = tasks (any depth, meetings and deleted excluded) whose ObjectiveId is
/// in the given, already subtree-expanded Module ids. Overlap = start &lt; to and end &gt; from (an open session: clock-in
/// &lt; to).</summary>
public interface ITeamTimeReadRepository
{
    Task<IReadOnlyList<TeamBlockRow>> ListBlocksForTasksAsync(Guid tenantId, IReadOnlyCollection<Guid> objectiveIds,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    Task<IReadOnlyList<TeamSessionRow>> ListSessionsForTasksAsync(Guid tenantId, IReadOnlyCollection<Guid> objectiveIds,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    Task<IReadOnlyList<TeamBlockRow>> ListBlocksForEmployeesAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    Task<IReadOnlyList<TeamSessionRow>> ListSessionsForEmployeesAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>The given non-meeting, non-deleted tasks.</summary>
    Task<IReadOnlyDictionary<Guid, TeamTaskRow>> GetTasksAsync(Guid tenantId, IReadOnlyCollection<Guid> taskIds,
        CancellationToken ct = default);

    /// <summary>An IANA zone id per employee, in ICalendarTimezoneResolver's order: the employee's DisplayTimezone, the legal
    /// entity's Timezone, else "UTC".</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetTimezonesAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct = default);

    /// <summary>Name ("First Last"), primary avatar and active flag per employee of this tenant.</summary>
    Task<IReadOnlyDictionary<Guid, TeamPersonRow>> GetPeopleAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct = default);
}
