using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Reports.RepositoryInterfaces;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Reports;

public class EfProductivityReportRepository : IProductivityReportRepository
{
    private readonly ApplicationDbContext _db;

    public EfProductivityReportRepository(ApplicationDbContext db) => _db = db;

    public Task<ProductivityAggregate> GetEmployeeAggregateAsync(
        Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct) =>
        AggregateAsync(tenantId, new[] { employeeId }, from, to, ct);

    public async Task<ProductivityAggregate?> GetDepartmentAggregateAsync(
        Guid tenantId, Guid departmentId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var departmentExists = await _db.Departments
            .AsNoTracking()
            .AnyAsync(d => d.TenantId == tenantId && d.Id == departmentId, ct);
        if (!departmentExists)
            return null;

        var employeeIds = await _db.Employees
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.DepartmentId == departmentId)
            .Select(e => e.Id)
            .ToListAsync(ct);

        return await AggregateAsync(tenantId, employeeIds, from, to, ct);
    }

    public async Task<ProductivityAggregate> GetTenantAggregateAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var employeeIds = await _db.Employees
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .Select(e => e.Id)
            .ToListAsync(ct);

        return await AggregateAsync(tenantId, employeeIds, from, to, ct);
    }

    private async Task<ProductivityAggregate> AggregateAsync(
        Guid tenantId, IReadOnlyList<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (employeeIds.Count == 0)
            return new ProductivityAggregate(0, 0, 0, 0, 0, 0, 0m, 0, 0, 0);

        var summaryAgg = await _db.ActivityDailySummaries
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && employeeIds.Contains(s.EmployeeId)
                        && s.Date >= from && s.Date <= to)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalActive = g.Sum(x => x.TotalActiveMinutes),
                TotalIdle = g.Sum(x => x.TotalIdleMinutes),
                TotalMeeting = g.Sum(x => x.TotalMeetingMinutes),
                Productive = g.Sum(x => x.ProductiveAppMinutes),
                Personal = g.Sum(x => x.PersonalAppMinutes),
                Unknown = g.Sum(x => x.UnknownAppMinutes),
                AvgScore = g.Average(x => x.ActivityScore),
                Days = g.Count()
            })
            .FirstOrDefaultAsync(ct);

        var (start, end) = UtcDayBounds(from, to);
        var workSessionAgg = await _db.EmployeeWorkSessions
            .AsNoTracking()
            .Where(w => w.TenantId == tenantId && employeeIds.Contains(w.UserId)
                        && w.ClockInAt >= start && w.ClockInAt < end)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                WorkedSeconds = g.Sum(x => x.AccumulatedWorkSeconds),
                BreakSeconds = g.Sum(x => x.AccumulatedBreakSeconds)
            })
            .FirstOrDefaultAsync(ct);

        return new ProductivityAggregate(
            summaryAgg?.TotalActive ?? 0,
            summaryAgg?.TotalIdle ?? 0,
            summaryAgg?.TotalMeeting ?? 0,
            summaryAgg?.Productive ?? 0,
            summaryAgg?.Personal ?? 0,
            summaryAgg?.Unknown ?? 0,
            summaryAgg?.AvgScore ?? 0m,
            (workSessionAgg?.WorkedSeconds ?? 0) / 60,
            (workSessionAgg?.BreakSeconds ?? 0) / 60,
            summaryAgg?.Days ?? 0);
    }

    /// <summary>
    /// A work session is attributed to the range by its clock-in date - matches how
    /// ActivityDailySummary is bucketed by calendar day, so both halves of the report
    /// use the same day-attribution rule.
    /// </summary>
    private static (DateTimeOffset Start, DateTimeOffset End) UtcDayBounds(DateOnly from, DateOnly to)
    {
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(1);
        return (start, end);
    }
}
