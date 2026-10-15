using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.Exceptions;

public interface ICalendarIncidentRepository
{
    /// <summary>The employee's incidents (tracked) whose occurrence starts at or after <paramref name="from"/>.</summary>
    Task<List<CalendarMeetingIncident>> ListForEmployeeAsync(Guid tenantId, Guid employeeId, DateTimeOffset from, CancellationToken ct);

    /// <summary>The incidents attached to one case, oldest first (the case's evidence).</summary>
    Task<IReadOnlyList<CalendarMeetingIncident>> ListForExceptionAsync(Guid tenantId, Guid exceptionId, CancellationToken ct);

    /// <summary>The incidents a meeting raised, oldest occurrence first (the organizer's Attendance tab, plan FC).</summary>
    Task<IReadOnlyList<CalendarMeetingIncident>> ListForEventAsync(Guid tenantId, Guid eventId, CancellationToken ct);

    /// <summary>Removes incidents never attached to a case that are older than <paramref name="before"/> (retention).</summary>
    Task<int> PurgeUnattachedAsync(Guid tenantId, DateTimeOffset before, CancellationToken ct);

    void Add(CalendarMeetingIncident incident);
    void Remove(CalendarMeetingIncident incident);
    Task SaveChangesAsync(CancellationToken ct);
}
