using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Settings.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Settings;

public class EfMonitoringFeatureTogglesRepository : IMonitoringFeatureTogglesRepository
{
    private readonly ApplicationDbContext _db;

    public EfMonitoringFeatureTogglesRepository(ApplicationDbContext db) => _db = db;

    public async Task<MonitoringSetting?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        await _db.MonitoringSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId
                && t.ScopeType == MonitoringSettingScopes.Tenant
                && t.ScopeId == tenantId, ct);

    public async Task<MonitoringSetting?> GetByLegalEntityIdAsync(
        Guid tenantId, Guid legalEntityId, bool includeTenantFallback, CancellationToken ct = default)
    {
        var exact = await _db.MonitoringSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId
                && t.ScopeType == MonitoringSettingScopes.LegalEntity
                && t.ScopeId == legalEntityId, ct);
        return exact ?? (includeTenantFallback ? await GetByTenantIdAsync(tenantId, ct) : null);
    }

    public Task<bool> LegalEntityExistsAsync(Guid tenantId, Guid legalEntityId, CancellationToken ct = default) =>
        _db.LegalEntities.AsNoTracking().AnyAsync(
            entity => entity.Id == legalEntityId && entity.TenantId == tenantId && entity.IsActive, ct);

    public async Task AddAsync(MonitoringSetting toggles, CancellationToken ct = default) =>
        await _db.MonitoringSettings.AddAsync(toggles, ct);

    public void Update(MonitoringSetting toggles) => _db.MonitoringSettings.Update(toggles);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) => await _db.SaveChangesAsync(ct);
}
