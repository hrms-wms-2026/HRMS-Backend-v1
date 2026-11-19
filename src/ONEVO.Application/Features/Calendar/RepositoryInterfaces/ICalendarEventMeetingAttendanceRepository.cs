using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.RepositoryInterfaces;

public interface ICalendarEventMeetingAttendanceRepository
{
    Task AddRangeAsync(IEnumerable<CalendarEventMeetingAttendance> attendances, CancellationToken ct = default);
    Task<IReadOnlyList<CalendarEventMeetingAttendance>> GetByMeetingIdAsync(Guid tenantId, Guid calendarEventMeetingId, CancellationToken ct = default);
}
