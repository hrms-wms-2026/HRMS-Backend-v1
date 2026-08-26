using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskClockingSessionRepository
{
    Task AddAsync(TaskClockingSession session, CancellationToken ct = default);
    Task<TaskClockingSession?> GetOpenSessionForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, Guid>> GetOpenSessionsForTasksAsync(Guid tenantId, IReadOnlyList<Guid> taskIds, CancellationToken ct = default);
    Task<TaskClockingSession?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TaskClockingSession>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
    void Update(TaskClockingSession session);
}
