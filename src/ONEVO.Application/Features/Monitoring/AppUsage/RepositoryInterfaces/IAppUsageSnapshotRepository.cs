using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;

namespace ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;

public interface IAppUsageSnapshotRepository
{
    Task AddRangeAsync(IEnumerable<AppUsageSnapshot> snapshots, CancellationToken ct);

    Task<IReadOnlyList<AppUsageSnapshot>> GetByEmployeeDateAsync(
        Guid tenantId, Guid employeeId, DateOnly date, int page, int pageSize, CancellationToken ct);

    Task<int> GetTotalCountAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct);

    Task<IReadOnlyList<AppUsageSnapshot>> GetAllByEmployeeDateAsync(
        Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct);
}
