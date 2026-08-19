using ONEVO.Domain.Features.Monitoring.Settings.Entities;

namespace ONEVO.Application.Features.Monitoring.Settings.RepositoryInterfaces;

public interface IMonitoringFeatureTogglesRepository
{
    Task<MonitoringFeatureToggles?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(MonitoringFeatureToggles toggles, CancellationToken ct = default);

    void Update(MonitoringFeatureToggles toggles);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
