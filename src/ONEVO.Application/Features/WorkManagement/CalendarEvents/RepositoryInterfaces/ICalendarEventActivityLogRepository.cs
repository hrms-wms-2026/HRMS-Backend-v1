using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;

public interface ICalendarEventActivityLogRepository
{
    Task AddAsync(CalendarEventActivityLog log, CancellationToken ct = default);
    Task<IReadOnlyList<CalendarEventActivityLog>> ListByEventIdAsync(Guid calendarEventId, CancellationToken ct = default);
}
