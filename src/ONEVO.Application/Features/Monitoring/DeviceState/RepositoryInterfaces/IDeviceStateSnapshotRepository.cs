using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;

namespace ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;

public interface IDeviceStateSnapshotRepository
{
    Task AddRangeAsync(IEnumerable<DeviceStateSnapshot> snapshots, CancellationToken ct);
}
