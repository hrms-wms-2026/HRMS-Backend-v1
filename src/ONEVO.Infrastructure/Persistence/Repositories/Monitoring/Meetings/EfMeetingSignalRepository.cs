using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Meetings;

public class EfMeetingSignalRepository : IMeetingSignalRepository
{
    private readonly ApplicationDbContext _db;

    public EfMeetingSignalRepository(ApplicationDbContext db) => _db = db;

    public async Task AddRangeAsync(IEnumerable<MeetingSignal> signals, CancellationToken ct)
        => await _db.MeetingSignals.AddRangeAsync(signals, ct);

    public async Task<IReadOnlyList<MeetingSignal>> GetByEmployeeDateAsync(
        Guid tenantId, Guid employeeId, DateOnly date, int page, int pageSize, CancellationToken ct)
    {
        var (start, end) = UtcDayBounds(date);

        return await _db.MeetingSignals
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

        return await _db.MeetingSignals
            .AsNoTracking()
            .CountAsync(s => s.TenantId == tenantId
                             && s.EmployeeId == employeeId
                             && s.CapturedAt >= start
                             && s.CapturedAt < end, ct);
    }

    public async Task<IReadOnlyList<MeetingSignal>> GetAllByEmployeeDateAsync(
        Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var (start, end) = UtcDayBounds(date);

        return await _db.MeetingSignals
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.EmployeeId == employeeId
                        && s.CapturedAt >= start && s.CapturedAt < end)
            .ToListAsync(ct);
    }

    private static (DateTimeOffset Start, DateTimeOffset End) UtcDayBounds(DateOnly date)
    {
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return (start, start.AddDays(1));
    }
}
