using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Queries.GetCalendarEventTasks;

public sealed class GetCalendarEventTasksQueryHandler : IRequestHandler<GetCalendarEventTasksQuery, Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ICalendarEventRepository _calendarEvents;
    private readonly IProjectMemberRepository _members;
    private readonly IObjectiveRepository _objectives;
    private readonly IModuleReadAccess _readAccess;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusRepository _taskStatuses;

    public GetCalendarEventTasksQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, ICalendarEventRepository calendarEvents,
        IProjectMemberRepository members, IObjectiveRepository objectives, IModuleReadAccess readAccess,
        IPermissionResolver permissionResolver, IWorkTaskRepository tasks, ITaskStatusRepository taskStatuses)
    {
        _currentUser = currentUser;
        _identity = identity;
        _calendarEvents = calendarEvents;
        _members = members;
        _objectives = objectives;
        _readAccess = readAccess;
        _permissionResolver = permissionResolver;
        _tasks = tasks;
        _taskStatuses = taskStatuses;
    }

    public async Task<Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>> Handle(GetCalendarEventTasksQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>.Forbidden("Authentication required.");
        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>.Forbidden("Tenant context missing.");

        var employeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (employeeId is null)
            return Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>.Forbidden("No employee record for the current user.");

        var calendarEvent = await _calendarEvents.GetByIdForTenantAsync(tenantId, request.CalendarEventId, ct);
        if (calendarEvent is null)
            return Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>.NotFound("Calendar event not found.");
        if (!await _members.HasActiveMembershipAsync(tenantId, calendarEvent.ProjectId, employeeId.Value, ct))
            return Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>.Forbidden("You do not have access to this project.");

        var wholeObjectiveIds = (await _calendarEvents.ListMembershipsForEventAsync(calendarEvent.Id, ct))
            .Select(m => m.ObjectiveId).ToHashSet();
        var directTaskIds = (await _calendarEvents.ListTaskMembershipsForEventAsync(calendarEvent.Id, ct))
            .Select(l => l.TaskId).ToHashSet();

        var projectTasks = await _tasks.GetByProjectAsync(tenantId, calendarEvent.ProjectId, ct);
        var memberTasks = projectTasks
            .Where(t => wholeObjectiveIds.Contains(t.ObjectiveId) || directTaskIds.Contains(t.Id))
            .ToList();
        if (memberTasks.Count == 0)
            return Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>.Success(Array.Empty<CalendarEventTaskSummaryResponse>());

        // Same per-objective read gate GetObjectiveTasksQueryHandler uses - a milestone can span
        // several objectives, and project membership alone is a weaker check than that endpoint's.
        var permissions = await _permissionResolver.ResolveAsync(_currentUser.UserId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("*");
        var objectives = await _objectives.GetAllByProjectIdAsync(tenantId, calendarEvent.ProjectId, ct);
        var objectivesById = objectives.ToDictionary(o => o.Id);
        if (!hasReadPermission)
        {
            var readableObjectiveIds = new HashSet<Guid>();
            foreach (var objectiveId in memberTasks.Select(t => t.ObjectiveId).Distinct())
            {
                if (objectivesById.TryGetValue(objectiveId, out var objective)
                    && await _readAccess.CanReadAsync(tenantId, objective, employeeId.Value, ct))
                    readableObjectiveIds.Add(objectiveId);
            }
            memberTasks = memberTasks.Where(t => readableObjectiveIds.Contains(t.ObjectiveId)).ToList();
        }
        if (memberTasks.Count == 0)
            return Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>.Success(Array.Empty<CalendarEventTaskSummaryResponse>());

        var statuses = await _taskStatuses.GetByIdsForTenantAsync(tenantId, memberTasks.Select(t => t.StatusId).Distinct().ToList(), ct);
        var statusById = statuses.ToDictionary(s => s.Id);

        var titleByObjectiveId = objectives.ToDictionary(o => o.Id, o => o.Title);

        var responses = memberTasks.Select(t =>
        {
            var status = statusById.GetValueOrDefault(t.StatusId);
            return new CalendarEventTaskSummaryResponse(
                t.Id, t.ShortId, t.Title, t.StatusId, status?.MarksTaskComplete ?? false, status?.Category ?? "not_started",
                t.ProgressPercent, t.ObjectiveId, titleByObjectiveId.GetValueOrDefault(t.ObjectiveId, ""));
        }).ToList();

        return Result<IReadOnlyList<CalendarEventTaskSummaryResponse>>.Success(responses);
    }
}
