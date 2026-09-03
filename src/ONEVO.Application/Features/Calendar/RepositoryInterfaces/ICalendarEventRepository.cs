using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.RepositoryInterfaces;

public interface ICalendarEventRepository
{
    Task AddAsync(CalendarEvent calendarEvent, CancellationToken ct = default);
    Task<CalendarEvent?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<CalendarEvent?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Events in [from, to] where the caller is the creator or a participant. Excludes
    /// recurring masters (Recurrence != "none" AND RecurrenceParentId == null) - those are
    /// expanded into virtual occurrences separately, never returned as their own literal row, to
    /// avoid double-counting a master's first occurrence. Excludes cancellation markers always.</summary>
    Task<IReadOnlyList<CalendarEvent>> GetInDateRangeForCallerAsync(
        Guid tenantId, Guid userId, Guid? employeeId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Recurring master rows the caller created or participates in, whose series could
    /// produce an occurrence at or before `to` - the caller expands each one's RecurrenceRule to
    /// find which occurrences actually fall in the query's [from, to] window.</summary>
    Task<IReadOnlyList<CalendarEvent>> GetRecurringMastersForCallerAsync(
        Guid tenantId, Guid userId, Guid? employeeId, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Every detached-occurrence and cancellation-marker row belonging to one master -
    /// tracked, since both the query-merge (read-only) and this-and-following re-parent (write)
    /// call sites use it.</summary>
    Task<IReadOnlyList<CalendarEvent>> GetChildrenForMasterAsync(Guid tenantId, Guid masterId, CancellationToken ct = default);

    /// <summary>The tracked child row (detached or cancellation-marker) for one exact occurrence,
    /// or null if that occurrence has never been edited/cancelled before.</summary>
    Task<CalendarEvent?> GetTrackedChildByOriginalStartAsync(
        Guid tenantId, Guid masterId, DateTimeOffset originalStart, CancellationToken ct = default);

    Task AddParticipantsAsync(IReadOnlyList<CalendarEventParticipant> participants, CancellationToken ct = default);

    /// <summary>Every participant row for the given events, grouped by EventId - used to attach
    /// participant summaries to items returned from GetCalendarEventsQueryHandler.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<CalendarEventParticipant>>> GetParticipantsForEventsAsync(
        Guid tenantId, IReadOnlyList<Guid> eventIds, CancellationToken ct = default);

    /// <summary>The tracked participant row for one (event, employee) pair, or null if that
    /// employee isn't a participant on this event.</summary>
    Task<CalendarEventParticipant?> GetTrackedParticipantAsync(
        Guid tenantId, Guid eventId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Same shape as GetInDateRangeForCallerAsync, but scoped to one specific employee's
    /// participation rather than the current caller - used for conflict-checking a participant
    /// who is not the person making the request.</summary>
    Task<IReadOnlyList<CalendarEvent>> GetInDateRangeForEmployeeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Same shape as GetRecurringMastersForCallerAsync, scoped to one specific employee's
    /// participation.</summary>
    Task<IReadOnlyList<CalendarEvent>> GetRecurringMastersForEmployeeAsync(
        Guid tenantId, Guid employeeId, DateTimeOffset to, CancellationToken ct = default);
    void Update(CalendarEvent calendarEvent);
    void Remove(CalendarEvent calendarEvent);
}
