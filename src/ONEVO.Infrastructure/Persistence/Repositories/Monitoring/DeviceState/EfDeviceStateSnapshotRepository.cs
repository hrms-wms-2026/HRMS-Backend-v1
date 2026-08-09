using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.DeviceState;

public class EfDeviceStateSnapshotRepository : IDeviceStateSnapshotRepository
{
    private readonly ApplicationDbContext _db;

    public EfDeviceStateSnapshotRepository(ApplicationDbContext db) => _db = db;

    public async Task AddRangeAsync(IEnumerable<DeviceStateSnapshot> snapshots, CancellationToken ct)
        => await _db.DeviceStateSnapshots.AddRangeAsync(snapshots, ct);
}
