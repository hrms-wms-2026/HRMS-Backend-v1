using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public sealed record OpenTaskClockingSessionSummary(Guid EmployeeId, DateTimeOffset ClockInAt);

public sealed record OpenEmployeeTaskSession(Guid TaskId, string TaskTitle);

public interface ITaskClockingSessionRepository
{
    Task AddAsync(TaskClockingSession session, CancellationToken ct = default);
    Task<TaskClockingSession?> GetOpenSessionForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, OpenTaskClockingSessionSummary>> GetOpenSessionsForTasksAsync(Guid tenantId, IReadOnlyList<Guid> taskIds, CancellationToken ct = default);
    Task<IReadOnlyList<OpenEmployeeTaskSession>> GetOpenSessionsForEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, int>> GetTotalClosedSessionMinutesForTasksAsync(Guid tenantId, IReadOnlyList<Guid> taskIds, CancellationToken ct = default);
    Task<TaskClockingSession?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TaskClockingSession>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
    void Update(TaskClockingSession session);
}
