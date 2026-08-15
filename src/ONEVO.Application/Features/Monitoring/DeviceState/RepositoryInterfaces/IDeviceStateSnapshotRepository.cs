using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;

namespace ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;

public interface IDeviceStateSnapshotRepository
{
    Task AddRangeAsync(IEnumerable<DeviceStateSnapshot> snapshots, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, DeviceStateSnapshot>> GetLatestForEmployeesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct);
}
