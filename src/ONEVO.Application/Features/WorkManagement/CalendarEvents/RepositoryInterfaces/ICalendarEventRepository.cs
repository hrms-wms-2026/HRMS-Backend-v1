using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;

public sealed record ActiveCalendarEventMembership(Guid CalendarEventId, Guid ObjectiveId, string Color);

public interface ICalendarEventRepository
{
    Task AddAsync(CalendarEvent calendarEvent, CancellationToken ct = default);
    Task AddMembershipsAsync(IReadOnlyCollection<CalendarEventObjective> memberships, CancellationToken ct = default);
    Task<CalendarEvent?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CalendarEvent>> ListActiveForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<CalendarEventObjective>> ListMembershipsForEventAsync(Guid calendarEventId, CancellationToken ct = default);
    Task<IReadOnlyList<ActiveCalendarEventMembership>> ListActiveMembershipsForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<ActiveCalendarEventMembership>> ListActiveMembershipsForObjectivesAsync(Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, CancellationToken ct = default);
    void RemoveMemberships(IReadOnlyCollection<CalendarEventObjective> memberships);
    void Update(CalendarEvent calendarEvent);
}
