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

    public async Task<CalendarEventMeeting?> GetByCalendarEventAsync(Guid tenantId, Guid calendarEventId, CancellationToken ct = default)
        => await _db.CalendarEventMeetings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.CalendarEventId == calendarEventId, ct);

    // The claim statements run outside any transaction, so each commits on its own and other requests
    // see the claim before this request calls the provider (spec §3.2.2).
    public async Task<bool> TryInsertProvisioningAsync(CalendarEventMeeting row, CancellationToken ct = default)
        => await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO calendar_event_meetings
                (id, tenant_id, calendar_event_id, external_calendar_connection_id, provider, external_meeting_id, join_url,
                 status, link_generation, provisioning_started_at, created_at)
            VALUES
                ({row.Id}, {row.TenantId}, {row.CalendarEventId}, {row.ExternalCalendarConnectionId}, {row.Provider}, '', '',
                 {CalendarEventMeetingStatuses.Provisioning}, 1, {row.ProvisioningStartedAt}, {row.CreatedAt})
            ON CONFLICT (calendar_event_id) DO NOTHING
            """, ct) == 1;

    public async Task<int?> TryClaimNewGenerationAsync(
        Guid tenantId, Guid meetingId, Guid connectionId, string provider, DateTimeOffset now, CancellationToken ct = default)
    {
        var generations = await _db.Database.SqlQuery<int>($"""
            UPDATE calendar_event_meetings
            SET status = {CalendarEventMeetingStatuses.Provisioning}, link_generation = link_generation + 1,
                provisioning_started_at = {now}, external_calendar_connection_id = {connectionId}, provider = {provider},
                external_meeting_id = '', join_url = '', organizer_join_url = NULL, passcode_or_pin = NULL,
                failure_reason = NULL, attendance_sync_status = NULL, last_attendance_synced_at = NULL, updated_at = {now}
            WHERE tenant_id = {tenantId} AND id = {meetingId}
              AND status IN ({CalendarEventMeetingStatuses.Failed}, {CalendarEventMeetingStatuses.Cancelled})
            RETURNING link_generation AS "Value"
            """).ToListAsync(ct);
        return generations.Count == 1 ? generations[0] : null;
    }

    public async Task<bool> TryMarkStaleProvisioningUnknownAsync(
        Guid tenantId, Guid meetingId, DateTimeOffset staleBefore, DateTimeOffset now, CancellationToken ct = default)
        => await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE calendar_event_meetings
            SET status = {CalendarEventMeetingStatuses.ProvisioningUnknown}, failure_reason = 'timeout_or_server_error', updated_at = {now}
            WHERE tenant_id = {tenantId} AND id = {meetingId}
              AND status = {CalendarEventMeetingStatuses.Provisioning} AND provisioning_started_at < {staleBefore}
            """, ct) == 1;

    public async Task<bool> TryClaimUnknownAsync(Guid tenantId, Guid meetingId, DateTimeOffset now, CancellationToken ct = default)
        => await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE calendar_event_meetings
            SET status = {CalendarEventMeetingStatuses.Provisioning}, provisioning_started_at = {now}, updated_at = {now}
            WHERE tenant_id = {tenantId} AND id = {meetingId} AND status = {CalendarEventMeetingStatuses.ProvisioningUnknown}
            """, ct) == 1;

    public async Task<bool> TryMarkUnknownUnresolvedAsync(Guid tenantId, Guid meetingId, DateTimeOffset now, CancellationToken ct = default)
        => await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE calendar_event_meetings
            SET status = {CalendarEventMeetingStatuses.Unresolved}, updated_at = {now}
            WHERE tenant_id = {tenantId} AND id = {meetingId} AND status = {CalendarEventMeetingStatuses.ProvisioningUnknown}
            """, ct) == 1;
}
