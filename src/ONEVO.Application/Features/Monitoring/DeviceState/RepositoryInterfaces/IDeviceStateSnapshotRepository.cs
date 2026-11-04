using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;

namespace ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;

public interface IDeviceStateSnapshotRepository
{
    Task AddRangeAsync(IEnumerable<DeviceStateSnapshot> snapshots, CancellationToken ct);

    /// <summary>
    /// captured_at values this device already stored among <paramref name="capturedAts"/> —
    /// lets ingest skip rows re-sent after a lost response.
    /// </summary>
    Task<IReadOnlySet<DateTimeOffset>> GetExistingCapturedAtsAsync(
        Guid tenantId,
        Guid agentDeviceId,
        IReadOnlyCollection<DateTimeOffset> capturedAts,
        CancellationToken ct);

    Task<IReadOnlyList<DeviceStateSnapshot>> GetByEmployeeDateAsync(
        Guid tenantId, Guid employeeId, DateOnly date, int page, int pageSize, CancellationToken ct);

    Task<int> GetTotalCountAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct);

    /// <summary>Distinct (tenant, employee) pairs with a snapshot after sinceUtc — "currently being monitored".</summary>
    Task<IReadOnlyList<(Guid TenantId, Guid EmployeeId)>> GetActiveEmployeeKeysAsync(
        DateTimeOffset sinceUtc, CancellationToken ct);

    /// <summary>Snapshots for one employee since sinceUtc, ordered oldest-first (enough history to detect a 120-min streak).</summary>
    Task<IReadOnlyList<DeviceStateSnapshot>> GetRecentAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset sinceUtc, CancellationToken ct);

    /// <summary>Snapshots for one employee in [from, to), oldest first, capped at <paramref name="max"/> -
    /// a company-local day for the exception evidence timeline.</summary>
    Task<IReadOnlyList<DeviceStateSnapshot>> ListForEmployeeInRangeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset from, DateTimeOffset to, int max, CancellationToken ct);
}
