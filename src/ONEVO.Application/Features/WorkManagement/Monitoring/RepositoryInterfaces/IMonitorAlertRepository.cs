using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.RepositoryInterfaces;

public interface IMonitorAlertRepository
{
    /// <summary>Open (unresolved) alerts of the project, tracked so the monitor can update them.</summary>
    Task<IReadOnlyList<MonitorAlert>> ListOpenTrackedForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    /// <summary>Open alerts of the project, newest first, read-only.</summary>
    Task<IReadOnlyList<MonitorAlert>> ListOpenForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    /// <summary>Open alerts of the project plus those resolved at or after <paramref name="resolvedSince"/>, read-only.</summary>
    Task<IReadOnlyList<MonitorAlert>> ListOpenOrResolvedSinceAsync(
        Guid tenantId, Guid projectId, DateTimeOffset resolvedSince, CancellationToken ct = default);

    Task<MonitorAlert?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task AddAsync(MonitorAlert alert, CancellationToken ct = default);
}
