using ONEVO.Domain.Features.Leave.Type.Entities;

namespace ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;

public interface ILeaveTypeRepository
{
    Task<IReadOnlyList<LeaveType>> ListAsync(Guid tenantId, bool includeInactive, CancellationToken ct = default);

    Task<LeaveType?> GetByIdAsync(Guid tenantId, Guid leaveTypeId, CancellationToken ct = default);

    Task<bool> ExistsByNameAsync(Guid tenantId, string name, Guid? excludingLeaveTypeId, CancellationToken ct = default);

    Task<bool> ExistsByCodeAsync(Guid tenantId, string code, Guid? excludingLeaveTypeId, CancellationToken ct = default);

    Task<int> CountPendingRequestsAsync(Guid tenantId, Guid leaveTypeId, CancellationToken ct = default);

    Task AddAsync(LeaveType leaveType, CancellationToken ct = default);

    void Update(LeaveType leaveType);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
