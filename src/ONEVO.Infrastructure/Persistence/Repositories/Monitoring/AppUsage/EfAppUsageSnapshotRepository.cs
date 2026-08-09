using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.AppUsage;

public class EfAppUsageSnapshotRepository : IAppUsageSnapshotRepository
{
    private readonly ApplicationDbContext _db;

    public EfAppUsageSnapshotRepository(ApplicationDbContext db) => _db = db;

    public async Task AddRangeAsync(IEnumerable<AppUsageSnapshot> snapshots, CancellationToken ct)
        => await _db.AppUsageSnapshots.AddRangeAsync(snapshots, ct);
}
