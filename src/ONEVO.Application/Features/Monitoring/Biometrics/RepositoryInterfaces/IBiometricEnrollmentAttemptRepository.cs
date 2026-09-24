using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;

public interface IBiometricEnrollmentAttemptRepository
{
    Task AddAsync(BiometricEnrollmentAttempt attempt, CancellationToken ct);

    Task<BiometricEnrollmentAttempt?> GetByIdAsync(
        Guid tenantId, Guid employeeId, Guid attemptId, CancellationToken ct);

    void Update(BiometricEnrollmentAttempt attempt);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
