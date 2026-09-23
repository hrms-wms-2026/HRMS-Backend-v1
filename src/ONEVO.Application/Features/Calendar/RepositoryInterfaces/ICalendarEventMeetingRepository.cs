using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.RepositoryInterfaces;

public interface ICalendarEventMeetingRepository
{
    Task AddAsync(CalendarEventMeeting meeting, CancellationToken ct = default);
    Task<CalendarEventMeeting?> GetTrackedByCalendarEventAsync(Guid tenantId, Guid calendarEventId, CancellationToken ct = default);

    /// <summary>Active meetings whose event has already ended and whose attendance has never
    /// been synced - TeamsAttendanceSyncJob's poll target.</summary>
    Task<IReadOnlyList<CalendarEventMeeting>> GetDueForAttendanceSyncAsync(Guid tenantId, CancellationToken ct = default);

    void Update(CalendarEventMeeting meeting);
}
