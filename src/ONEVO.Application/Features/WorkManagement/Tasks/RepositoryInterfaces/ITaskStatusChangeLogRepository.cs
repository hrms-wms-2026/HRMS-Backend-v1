using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskStatusChangeLogRepository
{
    Task AddAsync(TaskStatusChangeLog log, CancellationToken ct = default);
    Task<IReadOnlyList<TaskStatusChangeLog>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
}
