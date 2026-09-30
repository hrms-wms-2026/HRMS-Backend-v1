using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskDraftRepository
{
    Task AddAsync(TaskDraft draft, CancellationToken ct = default);

    /// <summary>Tracked; null unless it exists in the tenant AND belongs to ownerUserId.</summary>
    Task<TaskDraft?> GetOwnedAsync(Guid tenantId, Guid ownerUserId, Guid draftId, CancellationToken ct = default);

    /// <summary>Newest (UpdatedAt ?? CreatedAt) first, no tracking.</summary>
    Task<IReadOnlyList<TaskDraft>> ListOwnedAsync(Guid tenantId, Guid ownerUserId, CancellationToken ct = default);

    /// <summary>Soft delete via SoftDeleteInterceptor.</summary>
    void Remove(TaskDraft draft);
}
