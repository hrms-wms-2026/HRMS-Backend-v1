using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.RepositoryInterfaces;

public interface ICalendarEventMeetingRepository
{
    Task AddAsync(CalendarEventMeeting meeting, CancellationToken ct = default);
    Task<CalendarEventMeeting?> GetTrackedByCalendarEventAsync(Guid tenantId, Guid calendarEventId, CancellationToken ct = default);

    /// <summary>Active meetings for the given provider whose event has already ended and whose
    /// attendance has never been synced - {Teams,Zoom}AttendanceSyncJob's poll target. The
    /// provider filter keeps the two jobs from racing on each other's rows.</summary>
    Task<IReadOnlyList<CalendarEventMeeting>> GetDueForAttendanceSyncAsync(Guid tenantId, string provider, CancellationToken ct = default);

    void Update(CalendarEventMeeting meeting);
}
