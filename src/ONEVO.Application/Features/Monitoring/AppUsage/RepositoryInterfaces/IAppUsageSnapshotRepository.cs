using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;

namespace ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;

public interface IAppUsageSnapshotRepository
{
    Task AddRangeAsync(IEnumerable<AppUsageSnapshot> snapshots, CancellationToken ct);
}
