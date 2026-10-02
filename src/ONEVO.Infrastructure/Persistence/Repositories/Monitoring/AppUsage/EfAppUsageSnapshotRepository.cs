using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.AppUsage;

public class EfAppUsageSnapshotRepository : IAppUsageSnapshotRepository
{
    private readonly ApplicationDbContext _db;

    public EfAppUsageSnapshotRepository(ApplicationDbContext db) => _db = db;

    public async Task AddRangeAsync(IEnumerable<AppUsageSnapshot> snapshots, CancellationToken ct)
        => await _db.AppUsageSnapshots.AddRangeAsync(snapshots, ct);

    public async Task<IReadOnlyList<AppUsageSnapshot>> GetByEmployeeDateAsync(
        Guid tenantId, Guid employeeId, DateOnly date, int page, int pageSize, CancellationToken ct)
    {
        var (start, end) = UtcDayBounds(date);

        return await _db.AppUsageSnapshots
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

        return await _db.AppUsageSnapshots
            .AsNoTracking()
            .CountAsync(s => s.TenantId == tenantId
                             && s.EmployeeId == employeeId
                             && s.CapturedAt >= start
                             && s.CapturedAt < end, ct);
    }

    public async Task<IReadOnlyList<AppUsageSnapshot>> GetAllByEmployeeDateAsync(
        Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var (start, end) = UtcDayBounds(date);

        return await _db.AppUsageSnapshots
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.EmployeeId == employeeId
                        && s.CapturedAt >= start && s.CapturedAt < end)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AppProcessMinutesRow>> GetMinutesByProcessAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, CancellationToken ct)
        => await _db.AppUsageSnapshots.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.EmployeeId == employeeId && s.ProcessName != null
                        && s.CapturedAt >= fromUtc && s.CapturedAt < toUtcExclusive)
            .GroupBy(s => s.ProcessName!)
            .Select(g => new AppProcessMinutesRow(g.Key, g.Count(), g.Max(s => s.CapturedAt)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AppProcessSampleRow>> GetSamplesForProcessesAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive,
        IReadOnlyCollection<string> processNames, CancellationToken ct)
        => await _db.AppUsageSnapshots.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.EmployeeId == employeeId && s.ProcessName != null
                        && processNames.Contains(s.ProcessName)
                        && s.CapturedAt >= fromUtc && s.CapturedAt < toUtcExclusive)
            .OrderBy(s => s.CapturedAt)
            .Select(s => new AppProcessSampleRow(s.ProcessName!, s.CapturedAt))
            .ToListAsync(ct);

    private static (DateTimeOffset Start, DateTimeOffset End) UtcDayBounds(DateOnly date)
    {
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return (start, start.AddDays(1));
    }
}
