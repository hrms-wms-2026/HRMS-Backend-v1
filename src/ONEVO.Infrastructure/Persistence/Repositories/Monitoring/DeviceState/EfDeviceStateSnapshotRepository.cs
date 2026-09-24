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

    public async Task<IReadOnlyList<DeviceStateSnapshot>> GetByEmployeeDateAsync(
        Guid tenantId, Guid employeeId, DateOnly date, int page, int pageSize, CancellationToken ct)
    {
        var (start, end) = UtcDayBounds(date);

        return await _db.DeviceStateSnapshots
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId
                        && s.EmployeeId == employeeId
                        && s.CapturedAt >= start
                        && s.CapturedAt < end)
            .OrderBy(s => s.CapturedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<int> GetTotalCountAsync(
        Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var (start, end) = UtcDayBounds(date);

        return await _db.DeviceStateSnapshots
            .AsNoTracking()
            .CountAsync(s => s.TenantId == tenantId
                             && s.EmployeeId == employeeId
                             && s.CapturedAt >= start
                             && s.CapturedAt < end, ct);
    }

    public async Task<IReadOnlyList<(Guid TenantId, Guid EmployeeId)>> GetActiveEmployeeKeysAsync(
        DateTimeOffset sinceUtc, CancellationToken ct)
    {
        var rows = await _db.DeviceStateSnapshots
            .AsNoTracking()
            .Where(s => s.CapturedAt >= sinceUtc)
            .Select(s => new { s.TenantId, s.EmployeeId })
            .Distinct()
            .ToListAsync(ct);

        return rows.Select(r => (r.TenantId, r.EmployeeId)).ToList();
    }

    public async Task<IReadOnlyList<DeviceStateSnapshot>> GetRecentAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset sinceUtc, CancellationToken ct) =>
        await _db.DeviceStateSnapshots
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.EmployeeId == employeeId && s.CapturedAt >= sinceUtc)
            .OrderBy(s => s.CapturedAt)
            .ToListAsync(ct);

    private static (DateTimeOffset Start, DateTimeOffset End) UtcDayBounds(DateOnly date)
    {
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return (start, start.AddDays(1));
    }
}
