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

    /// <summary>Cancelled requests that were cancelled only from <c>PartialCancelEffectiveDate</c> on (a partial cancel
    /// sets Status = cancelled), so the days before that date are still taken. Same overlap rule as
    /// <see cref="ListApprovedCoveringAsync"/>.</summary>
    Task<IReadOnlyList<LeaveRequest>> ListPartiallyCancelledCoveringAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default);
}
