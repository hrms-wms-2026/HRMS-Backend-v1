using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.RepositoryInterfaces;

/// <summary>A meeting due for an attendance fetch, with its event's end (the 24 h cut-off counts from it).</summary>
public sealed record DueAttendanceSync(CalendarEventMeeting Meeting, DateTimeOffset EventEndDate);

public interface ICalendarEventMeetingRepository
{
    Task AddAsync(CalendarEventMeeting meeting, CancellationToken ct = default);
    Task<CalendarEventMeeting?> GetTrackedByCalendarEventAsync(Guid tenantId, Guid calendarEventId, CancellationToken ct = default);

    /// <summary>Active meetings for the given provider whose event has already ended and whose
    /// attendance has never been synced - {Teams,Zoom}AttendanceSyncJob's poll target. The
    /// provider filter keeps the two jobs from racing on each other's rows.</summary>
    Task<IReadOnlyList<DueAttendanceSync>> GetDueForAttendanceSyncAsync(Guid tenantId, string provider, CancellationToken ct = default);

    void Update(CalendarEventMeeting meeting);
}
