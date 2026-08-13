using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Biometrics;

public class EfBiometricRepository : IBiometricRepository
{
    private readonly ApplicationDbContext _db;

    public EfBiometricRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAttemptAsync(BiometricVerificationAttempt attempt, CancellationToken ct)
        => await _db.BiometricVerificationAttempts.AddAsync(attempt, ct);

    public async Task<BiometricVerificationAttempt?> FindAttemptAsync(Guid attemptId, Guid tenantId, CancellationToken ct)
        => await _db.BiometricVerificationAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.TenantId == tenantId, ct);

    public async Task<EmployeeBiometricProfile?> FindActiveProfileAsync(Guid employeeId, Guid tenantId, CancellationToken ct)
        => await _db.EmployeeBiometricProfiles
            .FirstOrDefaultAsync(p => p.EmployeeId == employeeId
                                    && p.TenantId == tenantId
                                    && p.Status == BiometricProfileStatus.Active, ct);

    public async Task AddProfileAsync(EmployeeBiometricProfile profile, CancellationToken ct)
        => await _db.EmployeeBiometricProfiles.AddAsync(profile, ct);

    public async Task SupersedeActiveProfileAsync(Guid employeeId, Guid tenantId, DateTimeOffset supersededAt, CancellationToken ct)
    {
        await _db.EmployeeBiometricProfiles
            .Where(p => p.EmployeeId == employeeId && p.TenantId == tenantId && p.Status == BiometricProfileStatus.Active)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, BiometricProfileStatus.Superseded)
                .SetProperty(p => p.SupersededAt, supersededAt)
                .SetProperty(p => p.UpdatedAt, supersededAt), ct);
    }
}
