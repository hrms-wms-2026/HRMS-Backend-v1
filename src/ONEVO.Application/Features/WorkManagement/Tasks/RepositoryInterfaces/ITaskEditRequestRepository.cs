using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskEditRequestRepository
{
    Task AddAsync(TaskEditRequest request, CancellationToken ct = default);
    Task<TaskEditRequest?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<TaskEditRequest?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Pending requests routed to a given Objective owner - the owner's own approval queue.
    /// Joins through tasks.objective_id to objectives.owner_id at the repository layer since
    /// TaskEditRequest has no owner or Objective column of its own.</summary>
    Task<IReadOnlyList<TaskEditRequest>> GetPendingForOwnerEmployeeIdAsync(Guid tenantId, Guid ownerEmployeeId, CancellationToken ct = default);

    void Update(TaskEditRequest request);
}
