using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetProjectCalendar;

public sealed class GetProjectCalendarQueryHandler
    : IRequestHandler<GetProjectCalendarQuery, Result<ProjectCalendarResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;
    private readonly IWorkTaskRepository _tasks;
    private readonly ICalendarEventRepository _calendarEvents;

    public GetProjectCalendarQueryHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IProjectMemberRepository members,
        IObjectiveRepository objectives,
        IWorkTaskRepository tasks,
        ICalendarEventRepository calendarEvents)
    {
        _currentUser = currentUser;
        _identity = identity;
        _members = members;
        _objectives = objectives;
        _tasks = tasks;
        _calendarEvents = calendarEvents;
    }

    public async Task<Result<ProjectCalendarResponse>> Handle(
        GetProjectCalendarQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ProjectCalendarResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<ProjectCalendarResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<ProjectCalendarResponse>.Forbidden("No employee record for the current user.");

        var memberships = await _members.ListForEmployeeInProjectAsync(tenantId, request.ProjectId, callerEmployeeId.Value, ct);
        var objectives = await _objectives.GetAllByProjectIdAsync(tenantId, request.ProjectId, ct);
        if (objectives.Count == 0)
            return Result<ProjectCalendarResponse>.Success(
                new ProjectCalendarResponse(Array.Empty<ProjectCalendarItemResponse>(), Array.Empty<ProjectCalendarEventBand>()));

        var objectivesById = objectives.ToDictionary(o => o.Id);
        var activeMembershipObjectiveIds = memberships.Where(m => m.IsActive).Select(m => m.ObjectiveId).ToHashSet();

        bool IsEffectiveManager(Objective objective)
        {
            Objective? cursor = objective;
            while (cursor is not null)
            {
                if (cursor.OwnerId == callerEmployeeId.Value || activeMembershipObjectiveIds.Contains(cursor.Id))
                    return true;

                cursor = cursor.ParentObjectiveId is { } parentId
                    ? objectivesById.GetValueOrDefault(parentId)
                    : null;
            }

            return false;
        }

        var wholeLinks = await _calendarEvents.ListActiveMembershipsForProjectAsync(tenantId, request.ProjectId, ct);
        var taskLinks = await _calendarEvents.ListActiveTaskMembershipsForProjectAsync(tenantId, request.ProjectId, ct);
        var eventHeaders = await _calendarEvents.ListActiveEventHeadersForProjectAsync(tenantId, request.ProjectId, ct);
        var headerById = eventHeaders.ToDictionary(h => h.EventId);

        var allTasks = await _tasks.GetByProjectAsync(tenantId, request.ProjectId, ct);
        var taskCountByObjective = allTasks.GroupBy(t => t.ObjectiveId).ToDictionary(g => g.Key, g => g.Count());

        var wholeEventsByObjective = wholeLinks
            .GroupBy(l => l.ObjectiveId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.CalendarEventId).ToHashSet());
        var partialCountsByObjective = taskLinks
            .GroupBy(l => l.ObjectiveId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.EventId).ToDictionary(e => e.Key, e => e.Count()));

        var modules = objectives.Select(objective =>
        {
            var total = taskCountByObjective.GetValueOrDefault(objective.Id, 0);
            var links = new List<ProjectCalendarEventLink>();
            var wholeEventIds = wholeEventsByObjective.GetValueOrDefault(objective.Id, new HashSet<Guid>());

            foreach (var eventId in wholeEventIds)
                if (headerById.TryGetValue(eventId, out var header))
                    links.Add(new ProjectCalendarEventLink(
                        eventId, header.Name, header.Color, header.StartDate, header.EndDate,
                        ProjectCalendarEventMemberships.Whole, total, total));

            if (partialCountsByObjective.TryGetValue(objective.Id, out var perEvent))
                foreach (var (eventId, count) in perEvent)
                    if (!wholeEventIds.Contains(eventId) && headerById.TryGetValue(eventId, out var header))
                        links.Add(new ProjectCalendarEventLink(
                            eventId, header.Name, header.Color, header.StartDate, header.EndDate,
                            ProjectCalendarEventMemberships.Partial, count, total));

            var canEdit = IsEffectiveManager(objective) && !objective.IsAchieved && !objective.IsDefault;
            return new ProjectCalendarItemResponse(
                objective.Id, objective.ProjectId, objective.ParentObjectiveId, objective.Title,
                objective.StartDate, objective.EndDate, objective.IsActive, objective.IsAchieved, canEdit, links);
        }).ToList();

        var contributingObjectivesByEvent = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var link in wholeLinks)
            (contributingObjectivesByEvent.TryGetValue(link.CalendarEventId, out var set)
                ? set : contributingObjectivesByEvent[link.CalendarEventId] = new HashSet<Guid>())
                .Add(link.ObjectiveId);
        foreach (var link in taskLinks)
            (contributingObjectivesByEvent.TryGetValue(link.EventId, out var set)
                ? set : contributingObjectivesByEvent[link.EventId] = new HashSet<Guid>())
                .Add(link.ObjectiveId);

        var bands = eventHeaders.Select(header =>
        {
            var contributors = contributingObjectivesByEvent.GetValueOrDefault(header.EventId, new HashSet<Guid>());
            var canEdit = contributors.Any(id =>
                objectivesById.TryGetValue(id, out var objective) && IsEffectiveManager(objective));
            return new ProjectCalendarEventBand(
                header.EventId, header.Name, header.Color, header.StartDate, header.EndDate, canEdit);
        }).ToList();

        return Result<ProjectCalendarResponse>.Success(new ProjectCalendarResponse(modules, bands));
    }
}
