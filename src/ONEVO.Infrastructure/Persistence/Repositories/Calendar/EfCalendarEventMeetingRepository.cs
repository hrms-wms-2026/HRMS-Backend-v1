using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Calendar;

public class EfCalendarEventMeetingRepository : ICalendarEventMeetingRepository
{
    private readonly ApplicationDbContext _db;
    private readonly IDateTimeProvider _dateTime;

    public EfCalendarEventMeetingRepository(ApplicationDbContext db, IDateTimeProvider dateTime)
    {
        _db = db;
        _dateTime = dateTime;
    }

    public async Task AddAsync(CalendarEventMeeting meeting, CancellationToken ct = default)
        => await _db.CalendarEventMeetings.AddAsync(meeting, ct);

    public async Task<CalendarEventMeeting?> GetTrackedByCalendarEventAsync(Guid tenantId, Guid calendarEventId, CancellationToken ct = default)
        => await _db.CalendarEventMeetings.FirstOrDefaultAsync(
            m => m.TenantId == tenantId && m.CalendarEventId == calendarEventId, ct);

    public async Task<IReadOnlyList<DueAttendanceSync>> GetDueForAttendanceSyncAsync(Guid tenantId, string provider, CancellationToken ct = default)
    {
        // Reports appear some time after a meeting ends: wait out the grace before asking.
        var endedBefore = _dateTime.UtcNow - AttendanceSyncPolicy.Grace;
        var rows = await (
            from meeting in _db.CalendarEventMeetings.AsNoTracking()
            join calendarEvent in _db.PersonalCalendarEvents.AsNoTracking() on meeting.CalendarEventId equals calendarEvent.Id
            where meeting.TenantId == tenantId
                && meeting.Provider == provider
                && meeting.Status == CalendarEventMeetingStatuses.Active
                && meeting.AttendanceSyncStatus == null
                && meeting.LastAttendanceSyncedAt == null
                && calendarEvent.EndDate < endedBefore
            select new { meeting, calendarEvent.EndDate }
        ).ToListAsync(ct);
        return rows.Select(r => new DueAttendanceSync(r.meeting, r.EndDate)).ToList();
    }

    public void Update(CalendarEventMeeting meeting) => _db.CalendarEventMeetings.Update(meeting);
}
