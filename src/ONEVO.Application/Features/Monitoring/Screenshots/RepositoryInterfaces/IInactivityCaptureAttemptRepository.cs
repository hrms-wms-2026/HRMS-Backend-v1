using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

namespace ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;

public interface IInactivityCaptureAttemptRepository
{
    Task<InactivityCaptureAttempt?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct);
    Task AddAsync(InactivityCaptureAttempt attempt, CancellationToken ct);
    Task<IReadOnlyList<InactivityCaptureAttempt>> GetByEmployeeRangeAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct);
    Task<Guid?> FindContainingWorkSessionAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset instantUtc,
        CancellationToken ct);
}
