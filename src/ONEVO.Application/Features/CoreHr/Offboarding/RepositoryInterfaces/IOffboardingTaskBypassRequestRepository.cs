using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;

public interface IOffboardingTaskBypassRequestRepository
{
    Task<OffboardingTaskBypassRequest?> GetTrackedByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<bool> HasPendingForTaskAsync(Guid tenantId, Guid employeeChecklistTaskId, CancellationToken ct = default);
    Task<IReadOnlyList<OffboardingTaskBypassRequest>> ListPendingByApproverAsync(Guid tenantId, Guid approverId, CancellationToken ct = default);
    Task AddAsync(OffboardingTaskBypassRequest request, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
