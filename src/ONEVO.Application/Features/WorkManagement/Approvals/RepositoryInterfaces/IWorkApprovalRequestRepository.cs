using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;

public interface IWorkApprovalRequestRepository
{
    Task AddAsync(WorkApprovalRequest request, CancellationToken ct = default);
    Task<WorkApprovalRequest?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<bool> HasPendingAsync(Guid tenantId, string targetType, Guid targetId, string actionType, CancellationToken ct = default);
    /// <summary>Newest first. requestedByEmployeeId and status are optional filters.</summary>
    Task<IReadOnlyList<WorkApprovalRequest>> ListByProjectAsync(
        Guid tenantId, Guid projectId, Guid? requestedByEmployeeId, string? status, CancellationToken ct = default);
    /// <summary>Tracked pending requests of one action type in a project - used by conflict sweeps.</summary>
    Task<IReadOnlyList<WorkApprovalRequest>> ListTrackedPendingByActionAsync(
        Guid tenantId, Guid projectId, string actionType, CancellationToken ct = default);
    void Update(WorkApprovalRequest request);
}
