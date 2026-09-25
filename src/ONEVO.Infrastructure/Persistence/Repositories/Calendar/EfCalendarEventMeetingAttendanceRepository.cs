using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Calendar;

public class EfCalendarEventMeetingAttendanceRepository : ICalendarEventMeetingAttendanceRepository
{
    private readonly ApplicationDbContext _db;

    public EfCalendarEventMeetingAttendanceRepository(ApplicationDbContext db) => _db = db;

    public async Task AddRangeAsync(IEnumerable<CalendarEventMeetingAttendance> attendances, CancellationToken ct = default)
        => await _db.CalendarEventMeetingAttendances.AddRangeAsync(attendances, ct);

    public async Task<IReadOnlyList<CalendarEventMeetingAttendance>> GetByMeetingIdAsync(Guid tenantId, Guid calendarEventMeetingId, CancellationToken ct = default)
        => await _db.CalendarEventMeetingAttendances.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.CalendarEventMeetingId == calendarEventMeetingId)
            .OrderBy(a => a.JoinedAt)
            .ToListAsync(ct);
}
