using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
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

    public async Task<IReadOnlyList<CalendarEventMeeting>> GetDueForAttendanceSyncAsync(Guid tenantId, CancellationToken ct = default)
    {
        var now = _dateTime.UtcNow;
        return await (
            from meeting in _db.CalendarEventMeetings.AsNoTracking()
            join calendarEvent in _db.PersonalCalendarEvents.AsNoTracking() on meeting.CalendarEventId equals calendarEvent.Id
            where meeting.TenantId == tenantId
                && meeting.Status == CalendarEventMeetingStatuses.Active
                && meeting.LastAttendanceSyncedAt == null
                && calendarEvent.EndDate < now
            select meeting
        ).ToListAsync(ct);
    }

    public void Update(CalendarEventMeeting meeting) => _db.CalendarEventMeetings.Update(meeting);
}
