using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskCommentReactionRepository
{
    Task AddAsync(TaskCommentReaction reaction, CancellationToken ct = default);
    Task RemoveAsync(TaskCommentReaction reaction, CancellationToken ct = default);
    Task<TaskCommentReaction?> GetAsync(Guid tenantId, Guid commentId, Guid employeeId, string emoji, CancellationToken ct = default);
    Task<IReadOnlyList<TaskCommentReaction>> GetForCommentIdsAsync(Guid tenantId, IReadOnlyList<Guid> commentIds, CancellationToken ct = default);
}
