using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;

/// <summary>Sum of ActiveSeconds of one employee's snapshots captured in one UTC half-hour slot.</summary>
public sealed record ActivitySlotRow(DateTimeOffset SlotStartUtc, int ActiveSeconds);

public interface IActivitySnapshotRepository
{
    Task AddRangeAsync(IEnumerable<ActivitySnapshot> snapshots, CancellationToken ct);

    /// <summary>
    /// Of the given capture instants, returns the ones this device already has a snapshot row for.
    /// Used to make ingest idempotent against a tray retry/resend of the same batch.
    /// </summary>
    Task<IReadOnlySet<DateTimeOffset>> GetExistingCapturedAtsAsync(
        Guid tenantId,
        Guid agentDeviceId,
        IReadOnlyCollection<DateTimeOffset> capturedAts,
        CancellationToken ct);

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

    /// <summary>
    /// Distinct (tenant_id, employee_id) pairs that have snapshots on the given UTC date.
    /// </summary>
    Task<IReadOnlyList<(Guid TenantId, Guid EmployeeId)>> GetEmployeeKeysForDateAsync(
        DateOnly date,
        CancellationToken ct);

    /// <summary>ActiveSeconds summed per UTC half-hour slot (by CapturedAt) in [fromUtc, toUtcExclusive).</summary>
    Task<IReadOnlyList<ActivitySlotRow>> GetActiveSecondsByHalfHourAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct);

    /// <summary>Latest CapturedAt with ActiveSeconds > 0 in the window, or null.</summary>
    Task<DateTimeOffset?> GetLastActiveAtAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct);

    /// <summary>Snapshots in the window ordered by CapturedAt, projected to CapturedAt, ActiveSeconds,
    /// IdleSeconds and ForegroundProcessName (the fields WorkPatternWindowClassifier reads).</summary>
    Task<IReadOnlyList<ActivitySnapshot>> GetWindowsByEmployeeRangeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct);
}
