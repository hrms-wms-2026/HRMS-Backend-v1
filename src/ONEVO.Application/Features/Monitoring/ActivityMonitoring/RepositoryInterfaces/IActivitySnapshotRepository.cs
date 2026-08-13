using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;

public interface IActivitySnapshotRepository
{
    Task AddRangeAsync(IEnumerable<ActivitySnapshot> snapshots, CancellationToken ct);

    Task<IReadOnlyList<ActivitySnapshot>> GetByEmployeeDateAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<int> GetTotalCountAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct);

    /// <summary>
    /// Returns all snapshots for a tenant+employee on a calendar date (UTC day bounds).
    /// Used by the daily aggregation job.
    /// </summary>
    Task<IReadOnlyList<ActivitySnapshot>> GetAllByEmployeeDateAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct);

    Task<IReadOnlyList<ActivitySnapshot>> GetAllByEmployeeCapturedRangeAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct);

    /// <summary>
    /// Distinct (tenant_id, employee_id) pairs that have snapshots on the given UTC date.
    /// </summary>
    Task<IReadOnlyList<(Guid TenantId, Guid EmployeeId)>> GetEmployeeKeysForDateAsync(
        DateOnly date,
        CancellationToken ct);
}
