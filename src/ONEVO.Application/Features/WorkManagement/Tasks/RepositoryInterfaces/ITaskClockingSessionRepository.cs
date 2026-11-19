using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public sealed record OpenTaskClockingSessionSummary(Guid EmployeeId, DateTimeOffset ClockInAt);

public sealed record OpenEmployeeTaskSession(Guid TaskId, string TaskTitle);

/// <summary>One clocking session joined with its task, status and project — for day summaries.</summary>
public sealed record EmployeeTaskSessionRow(
    Guid TaskId,
    string ShortId,
    string Title,
    string? ProjectName,
    int ProgressPercent,
    string? StatusName,
    string? StatusColor,
    string? StatusCategory,
    bool StatusMarksTaskComplete,
    DateTimeOffset? TaskCompletedAt,
    DateTimeOffset ClockInAt,
    DateTimeOffset? ClockOutAt);

public interface ITaskClockingSessionRepository
{
    Task AddAsync(TaskClockingSession session, CancellationToken ct = default);
    Task<TaskClockingSession?> GetOpenSessionForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, OpenTaskClockingSessionSummary>> GetOpenSessionsForTasksAsync(Guid tenantId, IReadOnlyList<Guid> taskIds, CancellationToken ct = default);
    Task<IReadOnlyList<OpenEmployeeTaskSession>> GetOpenSessionsForEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
    /// <summary>Sessions of one employee that overlap [fromUtc, toUtcExclusive), open sessions included.</summary>
    Task<IReadOnlyList<EmployeeTaskSessionRow>> GetForEmployeeOverlappingAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, int>> GetTotalClosedSessionMinutesForTasksAsync(Guid tenantId, IReadOnlyList<Guid> taskIds, CancellationToken ct = default);
    Task<TaskClockingSession?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TaskClockingSession>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
    void Update(TaskClockingSession session);
}
