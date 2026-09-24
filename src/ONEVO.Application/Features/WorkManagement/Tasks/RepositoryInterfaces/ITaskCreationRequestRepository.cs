using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskCreationRequestRepository
{
    Task AddAsync(TaskCreationRequest request, CancellationToken ct = default);
    Task<TaskCreationRequest?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<TaskCreationRequest?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Pending requests routed to a given Objective owner - the owner's own approval queue.
    /// Joins against objectives.owner_id at the repository layer since TaskCreationRequest has no
    /// owner column of its own (the owner is looked up live via the Objective, not snapshotted).</summary>
    Task<IReadOnlyList<TaskCreationRequest>> GetPendingForOwnerEmployeeIdAsync(Guid tenantId, Guid ownerEmployeeId, CancellationToken ct = default);

    void Update(TaskCreationRequest request);
}
