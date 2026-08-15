using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.DeviceState;

public class EfDeviceStateSnapshotRepository : IDeviceStateSnapshotRepository
{
    private readonly ApplicationDbContext _db;

    public EfDeviceStateSnapshotRepository(ApplicationDbContext db) => _db = db;

    public async Task AddRangeAsync(IEnumerable<DeviceStateSnapshot> snapshots, CancellationToken ct)
        => await _db.DeviceStateSnapshots.AddRangeAsync(snapshots, ct);

    public async Task<IReadOnlyDictionary<Guid, DeviceStateSnapshot>> GetLatestForEmployeesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct)
    {
        if (employeeIds.Count == 0)
            return new Dictionary<Guid, DeviceStateSnapshot>();

        var latestCapturedAtByEmployee = _db.DeviceStateSnapshots
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && employeeIds.Contains(s.EmployeeId))
            .GroupBy(s => new { s.TenantId, s.EmployeeId })
            .Select(g => new
            {
                g.Key.TenantId,
                g.Key.EmployeeId,
                CapturedAt = g.Max(s => s.CapturedAt)
            });

        var snapshots = await (
            from snapshot in _db.DeviceStateSnapshots.AsNoTracking()
            join latest in latestCapturedAtByEmployee
                on new { snapshot.TenantId, snapshot.EmployeeId, snapshot.CapturedAt }
                equals new { latest.TenantId, latest.EmployeeId, latest.CapturedAt }
            select snapshot)
            .ToListAsync(ct);

        return snapshots
            .GroupBy(s => s.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.CreatedAt).First());
    }
}
