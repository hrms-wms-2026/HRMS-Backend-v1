using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskEditLogRepository
{
    Task AddAsync(TaskEditLog log, CancellationToken ct = default);
    Task<IReadOnlyList<TaskEditLog>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
}
