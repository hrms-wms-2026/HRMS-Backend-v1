using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.RepositoryInterfaces;

public interface ICalendarEventRepository
{
    Task AddAsync(CalendarEvent calendarEvent, CancellationToken ct = default);
    Task<CalendarEvent?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<CalendarEvent?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Events in [from, to] where the caller is the creator (by UserId) or a
    /// participant (by EmployeeId) - the two ways of "owning" a calendar event in this pass.</summary>
    Task<IReadOnlyList<CalendarEvent>> GetInDateRangeForCallerAsync(
        Guid tenantId, Guid userId, Guid? employeeId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    Task AddParticipantsAsync(IReadOnlyList<CalendarEventParticipant> participants, CancellationToken ct = default);
    void Update(CalendarEvent calendarEvent);
    void Remove(CalendarEvent calendarEvent);
}
