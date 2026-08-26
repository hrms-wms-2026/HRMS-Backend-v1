using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskPercentageLogRepository
{
    Task AddAsync(TaskPercentageLog log, CancellationToken ct = default);
    Task<IReadOnlyList<TaskPercentageLog>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
}
