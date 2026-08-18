using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Settings.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Settings;

public class EfMonitoringFeatureTogglesRepository : IMonitoringFeatureTogglesRepository
{
    private readonly ApplicationDbContext _db;

    public EfMonitoringFeatureTogglesRepository(ApplicationDbContext db) => _db = db;

    public async Task<MonitoringFeatureToggles?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        await _db.MonitoringFeatureToggles
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);

    public async Task AddAsync(MonitoringFeatureToggles toggles, CancellationToken ct = default) =>
        await _db.MonitoringFeatureToggles.AddAsync(toggles, ct);

    public void Update(MonitoringFeatureToggles toggles) => _db.MonitoringFeatureToggles.Update(toggles);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) => await _db.SaveChangesAsync(ct);
}
