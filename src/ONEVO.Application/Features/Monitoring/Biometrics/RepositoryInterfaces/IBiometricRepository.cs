using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;

public interface IBiometricRepository
{
    Task AddAttemptAsync(BiometricVerificationAttempt attempt, CancellationToken ct);
    Task<BiometricVerificationAttempt?> FindAttemptAsync(Guid attemptId, Guid tenantId, CancellationToken ct);

    Task<EmployeeBiometricProfile?> FindActiveProfileAsync(Guid employeeId, Guid tenantId, CancellationToken ct);
    Task AddProfileAsync(EmployeeBiometricProfile profile, CancellationToken ct);

    /// <summary>Marks the current Active profile (if any) Superseded. No-op if none exists.</summary>
    Task SupersedeActiveProfileAsync(Guid employeeId, Guid tenantId, DateTimeOffset supersededAt, CancellationToken ct);
}
