using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskCommentRepository
{
    Task AddAsync(TaskComment comment, CancellationToken ct = default);

    /// <summary>Returns a change-tracked entity — callers may mutate it directly and call
    /// IUnitOfWork.SaveChangesAsync rather than a separate Update method.</summary>
    Task<TaskComment?> GetByIdForTenantAsync(Guid tenantId, Guid commentId, CancellationToken ct = default);

    /// <summary>All comments for the task (top-level and replies, including soft-deleted
    /// ones) ordered oldest-first — filtering/grouping is the query handler's job.</summary>
    Task<IReadOnlyList<TaskComment>> GetForTaskAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);
}
