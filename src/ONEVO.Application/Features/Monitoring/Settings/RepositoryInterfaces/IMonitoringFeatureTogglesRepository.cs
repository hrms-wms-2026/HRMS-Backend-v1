using ONEVO.Domain.Features.Monitoring.Settings.Entities;

namespace ONEVO.Application.Features.Monitoring.Settings.RepositoryInterfaces;

/// <summary>Company monitoring defaults: the base rows (legal entity, tenant fallback) of monitoring_settings.</summary>
public interface IMonitoringFeatureTogglesRepository
{
    /// <summary>The retained tenant fallback row (scope "tenant").</summary>
    Task<MonitoringSetting?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    Task<MonitoringSetting?> GetByLegalEntityIdAsync(
        Guid tenantId, Guid legalEntityId, bool includeTenantFallback, CancellationToken ct = default);

    Task<bool> LegalEntityExistsAsync(Guid tenantId, Guid legalEntityId, CancellationToken ct = default);

    Task AddAsync(MonitoringSetting toggles, CancellationToken ct = default);

    void Update(MonitoringSetting toggles);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
