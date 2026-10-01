using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

public interface ITaskStatusChangeRequestRepository
{
    Task AddAsync(TaskStatusChangeRequest request, CancellationToken ct = default);
    Task<TaskStatusChangeRequest?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<TaskStatusChangeRequest?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>All pending requests for a project, oldest first.</summary>
    Task<IReadOnlyList<TaskStatusChangeRequest>> ListPendingForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    /// <summary>All pending requests across every project in the tenant, oldest first. Used by the
    /// My Team "Approvals &amp; Exceptions" action-item source, which has no single project in
    /// context and must filter down to the caller's approver projects itself.</summary>
    Task<IReadOnlyList<TaskStatusChangeRequest>> ListAllPendingAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Tracked pending requests for a project, for the conflict sweep to mark Outdated.</summary>
    Task<IReadOnlyList<TaskStatusChangeRequest>> ListTrackedPendingForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    void Update(TaskStatusChangeRequest request);
}
