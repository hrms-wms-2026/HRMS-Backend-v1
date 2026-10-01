using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;

namespace ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;

/// <summary>Samples (= minutes, one per 60s) and last sample time of one process in a window.</summary>
public sealed record AppProcessMinutesRow(string ProcessName, int Samples, DateTimeOffset LastCapturedAt);

public sealed record AppProcessSampleRow(string ProcessName, DateTimeOffset CapturedAt);

public interface IAppUsageSnapshotRepository
{
    Task AddRangeAsync(IEnumerable<AppUsageSnapshot> snapshots, CancellationToken ct);

    Task<IReadOnlyList<AppUsageSnapshot>> GetByEmployeeDateAsync(
        Guid tenantId, Guid employeeId, DateOnly date, int page, int pageSize, CancellationToken ct);

    Task<int> GetTotalCountAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct);

    Task<IReadOnlyList<AppUsageSnapshot>> GetAllByEmployeeDateAsync(
        Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct);

    Task<IReadOnlyList<AppProcessMinutesRow>> GetMinutesByProcessAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct);

    Task<IReadOnlyList<AppProcessSampleRow>> GetSamplesForProcessesAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive,
        IReadOnlyCollection<string> processNames, CancellationToken ct);
}
