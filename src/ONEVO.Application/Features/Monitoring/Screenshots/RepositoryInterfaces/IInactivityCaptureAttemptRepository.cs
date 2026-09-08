using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

namespace ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;

public interface IInactivityCaptureAttemptRepository
{
    void Add(InactivityCaptureAttempt attempt);

    Task<InactivityCaptureAttempt?> GetByIdAsync(Guid tenantId, Guid attemptId, CancellationToken ct);
}
