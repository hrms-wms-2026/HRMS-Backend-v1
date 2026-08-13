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

    public async Task<IReadOnlyList<ActivitySnapshot>> GetAllByEmployeeCapturedRangeAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct)
        => await _db.ActivitySnapshots
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId
                        && s.EmployeeId == employeeId
                        && s.CapturedAt >= fromUtc
                        && s.CapturedAt < toUtc)
            .OrderBy(s => s.CapturedAt)
            .ToListAsync(ct);

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

    private static (DateTimeOffset Start, DateTimeOffset End) UtcDayBounds(DateOnly date)
    {
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = start.AddDays(1);
        return (start, end);
    }
}
