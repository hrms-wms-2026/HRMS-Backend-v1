using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Biometrics.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Biometrics;

public class EfBiometricProfileRepository : IBiometricProfileRepository
{
    private readonly ApplicationDbContext _db;

    public EfBiometricProfileRepository(ApplicationDbContext db) => _db = db;

    public async Task<BiometricProfile?> GetByEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken ct) =>
        await _db.BiometricProfiles.FirstOrDefaultAsync(p => p.TenantId == tenantId && p.EmployeeId == employeeId, ct);

    public async Task AddAsync(BiometricProfile profile, CancellationToken ct)
        => await _db.BiometricProfiles.AddAsync(profile, ct);

    public void Update(BiometricProfile profile) => _db.BiometricProfiles.Update(profile);

    public async Task<int> SaveChangesAsync(CancellationToken ct) => await _db.SaveChangesAsync(ct);
}
