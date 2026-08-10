using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;

public interface IObjectiveChangeRequestRepository
{
    Task AddAsync(ObjectiveChangeRequest request, CancellationToken ct = default);

    Task<ObjectiveChangeRequest?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<bool> HasPendingForObjectiveAsync(Guid tenantId, Guid objectiveId, CancellationToken ct = default);

    Task<IReadOnlyList<ObjectiveChangeRequest>> ListPendingForApproverAsync(Guid tenantId, Guid reportingManagerId, CancellationToken ct = default);

    void Update(ObjectiveChangeRequest request);
}
