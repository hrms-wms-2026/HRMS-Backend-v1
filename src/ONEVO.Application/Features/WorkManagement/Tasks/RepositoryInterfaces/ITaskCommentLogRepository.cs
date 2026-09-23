using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskCommentLogRepository
{
    Task AddAsync(TaskCommentLog log, CancellationToken ct = default);
    Task<IReadOnlyList<TaskCommentLog>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
}
