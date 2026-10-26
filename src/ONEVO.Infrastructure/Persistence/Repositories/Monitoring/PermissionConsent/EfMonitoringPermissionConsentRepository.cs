using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.PermissionConsent.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.PermissionConsent.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.PermissionConsent;

public class EfMonitoringPermissionConsentRepository : IMonitoringPermissionConsentRepository
{
    private readonly ApplicationDbContext _db;

    public EfMonitoringPermissionConsentRepository(ApplicationDbContext db) => _db = db;

    public async Task<MonitoringPermissionConsent?> GetLatestAsync(Guid tenantId, Guid employeeId, CancellationToken ct) =>
        await _db.MonitoringPermissionConsents
            .Where(c => c.TenantId == tenantId && c.EmployeeId == employeeId)
            .OrderByDescending(c => c.AcceptedAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(MonitoringPermissionConsent consent, CancellationToken ct)
        => await _db.MonitoringPermissionConsents.AddAsync(consent, ct);

    public async Task<int> SaveChangesAsync(CancellationToken ct) => await _db.SaveChangesAsync(ct);
}
