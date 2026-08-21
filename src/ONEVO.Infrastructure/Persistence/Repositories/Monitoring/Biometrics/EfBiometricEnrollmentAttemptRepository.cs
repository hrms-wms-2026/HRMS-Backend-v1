using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Biometrics;

public class EfBiometricEnrollmentAttemptRepository : IBiometricEnrollmentAttemptRepository
{
    private readonly ApplicationDbContext _db;

    public EfBiometricEnrollmentAttemptRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(BiometricEnrollmentAttempt attempt, CancellationToken ct)
        => await _db.BiometricEnrollmentAttempts.AddAsync(attempt, ct);

    public async Task<BiometricEnrollmentAttempt?> GetByIdAsync(
        Guid tenantId, Guid employeeId, Guid attemptId, CancellationToken ct) =>
        await _db.BiometricEnrollmentAttempts
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.EmployeeId == employeeId && a.Id == attemptId, ct);

    public void Update(BiometricEnrollmentAttempt attempt) => _db.BiometricEnrollmentAttempts.Update(attempt);

    public async Task<int> SaveChangesAsync(CancellationToken ct) => await _db.SaveChangesAsync(ct);
}
