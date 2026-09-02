using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.CalendarEvents.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

public sealed class EfCalendarEventRepository : ICalendarEventRepository
{
    private readonly ApplicationDbContext _db;

    public EfCalendarEventRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
        => await _db.CalendarEvents.AddAsync(calendarEvent, ct);

    public async Task AddMembershipsAsync(IReadOnlyCollection<CalendarEventObjective> memberships, CancellationToken ct = default)
    {
        if (memberships.Count > 0)
            await _db.CalendarEventObjectives.AddRangeAsync(memberships, ct);
    }

    public async Task AddTaskMembershipsAsync(IReadOnlyCollection<CalendarEventTask> memberships, CancellationToken ct = default)
    {
        if (memberships.Count > 0)
            await _db.CalendarEventTasks.AddRangeAsync(memberships, ct);
    }

    public async Task<CalendarEvent?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        => await _db.CalendarEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == id, ct);

    public async Task<IReadOnlyList<CalendarEvent>> ListActiveForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
        => await _db.CalendarEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.ProjectId == projectId && e.Status == CalendarEventStatuses.Active)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CalendarEventObjective>> ListMembershipsForEventAsync(Guid calendarEventId, CancellationToken ct = default)
        => await _db.CalendarEventObjectives.AsNoTracking()
            .Where(m => m.CalendarEventId == calendarEventId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CalendarEventTask>> ListTaskMembershipsForEventAsync(Guid calendarEventId, CancellationToken ct = default)
        => await _db.CalendarEventTasks.AsNoTracking()
            .Where(m => m.CalendarEventId == calendarEventId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ActiveCalendarEventTaskLink>> ListActiveTaskLinksForTasksAsync(
        Guid tenantId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default)
    {
        if (taskIds.Count == 0)
            return Array.Empty<ActiveCalendarEventTaskLink>();

        return await (
            from link in _db.CalendarEventTasks.AsNoTracking()
            join calendarEvent in _db.CalendarEvents.AsNoTracking()
                on link.CalendarEventId equals calendarEvent.Id
            where taskIds.Contains(link.TaskId)
                && calendarEvent.TenantId == tenantId
                && calendarEvent.Status == CalendarEventStatuses.Active
            select new ActiveCalendarEventTaskLink(calendarEvent.Id, link.TaskId, calendarEvent.Name))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ActiveCalendarEventMembership>> ListActiveMembershipsForProjectAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
        => await (
            from membership in _db.CalendarEventObjectives.AsNoTracking()
            join calendarEvent in _db.CalendarEvents.AsNoTracking()
                on membership.CalendarEventId equals calendarEvent.Id
            where calendarEvent.TenantId == tenantId
                && calendarEvent.ProjectId == projectId
                && calendarEvent.Status == CalendarEventStatuses.Active
            select new ActiveCalendarEventMembership(calendarEvent.Id, membership.ObjectiveId, calendarEvent.Color))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ActiveEventHeader>> ListActiveEventHeadersForProjectAsync(
        Guid tenantId, Guid projectId, CancellationToken ct = default)
        => await _db.CalendarEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.ProjectId == projectId && e.Status == CalendarEventStatuses.Active)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new ActiveEventHeader(e.Id, e.Name, e.Color, e.StartDate, e.EndDate))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ActiveEventTaskMembership>> ListActiveTaskMembershipsForProjectAsync(
        Guid tenantId, Guid projectId, CancellationToken ct = default)
        => await (
            from link in _db.CalendarEventTasks.AsNoTracking()
            join calendarEvent in _db.CalendarEvents.AsNoTracking()
                on link.CalendarEventId equals calendarEvent.Id
            join task in _db.WorkTasks.AsNoTracking()
                on link.TaskId equals task.Id
            where calendarEvent.TenantId == tenantId
                && calendarEvent.ProjectId == projectId
                && calendarEvent.Status == CalendarEventStatuses.Active
            select new ActiveEventTaskMembership(calendarEvent.Id, link.TaskId, task.ObjectiveId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ActiveCalendarEventMembership>> ListActiveMembershipsForObjectivesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> objectiveIds, CancellationToken ct = default)
    {
        if (objectiveIds.Count == 0)
            return Array.Empty<ActiveCalendarEventMembership>();

        return await (
            from membership in _db.CalendarEventObjectives.AsNoTracking()
            join calendarEvent in _db.CalendarEvents.AsNoTracking()
                on membership.CalendarEventId equals calendarEvent.Id
            where objectiveIds.Contains(membership.ObjectiveId)
                && calendarEvent.TenantId == tenantId
                && calendarEvent.Status == CalendarEventStatuses.Active
            select new ActiveCalendarEventMembership(calendarEvent.Id, membership.ObjectiveId, calendarEvent.Color))
            .ToListAsync(ct);
    }

    public void RemoveMemberships(IReadOnlyCollection<CalendarEventObjective> memberships)
    {
        if (memberships.Count > 0)
            _db.CalendarEventObjectives.RemoveRange(memberships);
    }

    public void RemoveTaskMemberships(IReadOnlyCollection<CalendarEventTask> memberships)
    {
        if (memberships.Count > 0)
            _db.CalendarEventTasks.RemoveRange(memberships);
    }

    public void Update(CalendarEvent calendarEvent) => _db.CalendarEvents.Update(calendarEvent);
}
