using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;

public interface ILeaveRequestReadRepository
{
    Task<IReadOnlyList<LeaveRequest>> ListApprovedCoveringAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default);
}
