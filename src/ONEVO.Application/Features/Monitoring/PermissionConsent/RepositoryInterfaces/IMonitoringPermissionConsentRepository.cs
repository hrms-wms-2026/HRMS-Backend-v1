using ONEVO.Domain.Features.Monitoring.PermissionConsent.Entities;

namespace ONEVO.Application.Features.Monitoring.PermissionConsent.RepositoryInterfaces;

public interface IMonitoringPermissionConsentRepository
{
    /// <summary>The employee's most recent consent, or null when they never accepted.</summary>
    Task<MonitoringPermissionConsent?> GetLatestAsync(Guid tenantId, Guid employeeId, CancellationToken ct);

    Task AddAsync(MonitoringPermissionConsent consent, CancellationToken ct);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
