using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;

public sealed record ActiveCalendarEventMembership(Guid CalendarEventId, Guid ObjectiveId, string Color);

/// <summary>An active event a task is currently linked to directly (spec §2, R1).</summary>
public sealed record ActiveCalendarEventTaskLink(Guid CalendarEventId, Guid TaskId, string EventName);

public interface ICalendarEventRepository
{
    Task AddAsync(CalendarEvent calendarEvent, CancellationToken ct = default);
    Task AddMembershipsAsync(IReadOnlyCollection<CalendarEventObjective> memberships, CancellationToken ct = default);
    Task AddTaskMembershipsAsync(IReadOnlyCollection<CalendarEventTask> memberships, CancellationToken ct = default);
    Task<CalendarEvent?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CalendarEvent>> ListActiveForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<CalendarEventObjective>> ListMembershipsForEventAsync(Guid calendarEventId, CancellationToken ct = default);
    Task<IReadOnlyList<CalendarEventTask>> ListTaskMembershipsForEventAsync(Guid calendarEventId, CancellationToken ct = default);
    Task<IReadOnlyList<ActiveCalendarEventMembership>> ListActiveMembershipsForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<ActiveCalendarEventMembership>> ListActiveMembershipsForObjectivesAsync(Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, CancellationToken ct = default);
    Task<IReadOnlyList<ActiveCalendarEventTaskLink>> ListActiveTaskLinksForTasksAsync(Guid tenantId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default);
    void RemoveMemberships(IReadOnlyCollection<CalendarEventObjective> memberships);
    void RemoveTaskMemberships(IReadOnlyCollection<CalendarEventTask> memberships);
    void Update(CalendarEvent calendarEvent);
}
