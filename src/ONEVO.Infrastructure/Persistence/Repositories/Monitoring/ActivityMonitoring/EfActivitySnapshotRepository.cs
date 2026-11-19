using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.ActivityMonitoring;

public class EfActivitySnapshotRepository : IActivitySnapshotRepository
{
    private readonly ApplicationDbContext _db;

    public EfActivitySnapshotRepository(ApplicationDbContext db) => _db = db;

    public async Task AddRangeAsync(IEnumerable<ActivitySnapshot> snapshots, CancellationToken ct)
        => await _db.ActivitySnapshots.AddRangeAsync(snapshots, ct);

    public async Task<IReadOnlySet<DateTimeOffset>> GetExistingCapturedAtsAsync(
        Guid tenantId,
        Guid agentDeviceId,
        IReadOnlyCollection<DateTimeOffset> capturedAts,
        CancellationToken ct)
    {
        if (capturedAts.Count == 0)
            return new HashSet<DateTimeOffset>();

        var existing = await _db.ActivitySnapshots
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId
                        && s.AgentDeviceId == agentDeviceId
                        && capturedAts.Contains(s.CapturedAt))
            .Select(s => s.CapturedAt)
            .ToListAsync(ct);

        return existing.ToHashSet();
    }

    public async Task<IReadOnlyList<ActivitySnapshot>> GetByEmployeeDateAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var (start, end) = UtcDayBounds(date);

        return await _db.ActivitySnapshots
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
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct)
    {
        var (start, end) = UtcDayBounds(date);

        return await _db.ActivitySnapshots
            .AsNoTracking()
            .CountAsync(s => s.TenantId == tenantId
                             && s.EmployeeId == employeeId
                             && s.CapturedAt >= start
                             && s.CapturedAt < end, ct);
    }

    public async Task<IReadOnlyList<ActivitySnapshot>> GetAllByEmployeeDateAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly date,
        CancellationToken ct)
    {
        var (start, end) = UtcDayBounds(date);

        return await _db.ActivitySnapshots
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId
                        && s.EmployeeId == employeeId
                        && s.CapturedAt >= start
                        && s.CapturedAt < end)
            .OrderBy(s => s.CapturedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<(Guid TenantId, Guid EmployeeId)>> GetEmployeeKeysForDateAsync(
        DateOnly date,
        CancellationToken ct)
    {
        var (start, end) = UtcDayBounds(date);

        var rows = await _db.ActivitySnapshots
            .AsNoTracking()
            .Where(s => s.CapturedAt >= start && s.CapturedAt < end)
            .Select(s => new { s.TenantId, s.EmployeeId })
            .Distinct()
            .ToListAsync(ct);

        return rows.Select(r => (r.TenantId, r.EmployeeId)).ToList();
    }

    public async Task<IReadOnlyList<ActivitySlotRow>> GetActiveSecondsByHalfHourAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct)
    {
        // Grouped in SQL (at most 48 slots/day) - never loads the raw rows.
        var slots = await _db.ActivitySnapshots.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.EmployeeId == employeeId
                        && s.CapturedAt >= fromUtc && s.CapturedAt < toUtcExclusive)
            .GroupBy(s => new { s.CapturedAt.Year, s.CapturedAt.Month, s.CapturedAt.Day, s.CapturedAt.Hour, Half = s.CapturedAt.Minute / 30 })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, g.Key.Hour, g.Key.Half, Active = g.Sum(s => s.ActiveSeconds) })
            .ToListAsync(ct);

        return slots
            .Select(s => new ActivitySlotRow(new DateTimeOffset(s.Year, s.Month, s.Day, s.Hour, s.Half * 30, 0, TimeSpan.Zero), s.Active))
            .ToList();
    }

    public async Task<DateTimeOffset?> GetLastActiveAtAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct)
        => await _db.ActivitySnapshots.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.EmployeeId == employeeId && s.ActiveSeconds > 0
                        && s.CapturedAt >= fromUtc && s.CapturedAt < toUtcExclusive)
            .OrderByDescending(s => s.CapturedAt)
            .Select(s => (DateTimeOffset?)s.CapturedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ActivitySnapshot>> GetWindowsByEmployeeRangeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct)
        => await _db.ActivitySnapshots.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.EmployeeId == employeeId
                        && s.CapturedAt >= fromUtc && s.CapturedAt < toUtcExclusive)
            .OrderBy(s => s.CapturedAt)
            .Select(s => new ActivitySnapshot
            {
                TenantId = s.TenantId, EmployeeId = s.EmployeeId, CapturedAt = s.CapturedAt,
                ActiveSeconds = s.ActiveSeconds, IdleSeconds = s.IdleSeconds, ForegroundProcessName = s.ForegroundProcessName
            })
            .ToListAsync(ct);

    private static (DateTimeOffset Start, DateTimeOffset End) UtcDayBounds(DateOnly date)
    {
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = start.AddDays(1);
        return (start, end);
    }
}
