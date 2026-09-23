using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;

public interface IBiometricProfileRepository
{
    Task<BiometricProfile?> GetByEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct);

    Task AddAsync(BiometricProfile profile, CancellationToken ct);

    void Update(BiometricProfile profile);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
