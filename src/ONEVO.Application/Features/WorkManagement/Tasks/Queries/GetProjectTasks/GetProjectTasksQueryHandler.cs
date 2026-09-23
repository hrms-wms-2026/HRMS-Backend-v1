using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.CalendarEvents.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetProjectTasks;

public sealed class GetProjectTasksQueryHandler : IRequestHandler<GetProjectTasksQuery, Result<IReadOnlyList<WorkTaskResponse>>>
{
    private static readonly TimeSpan AvatarUrlExpiry = TimeSpan.FromMinutes(15);

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IFileStorageService _fileStorage;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly ITaskClockingSessionRepository _sessions;
    private readonly ICalendarEventRepository _calendarEvents;
    private readonly ITaskStatusRepository _statuses;

    public GetProjectTasksQueryHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IFileStorageService fileStorage,
        IProjectRepository projects,
        IProjectMemberRepository members,
        IPermissionResolver permissionResolver,
        IWorkTaskRepository tasks,
        ITaskAssignmentRepository assignments,
        ITaskClockingSessionRepository sessions,
        ICalendarEventRepository calendarEvents,
        ITaskStatusRepository statuses)
    {
        _currentUser = currentUser;
        _identity = identity;
        _fileStorage = fileStorage;
        _projects = projects;
        _members = members;
        _permissionResolver = permissionResolver;
        _tasks = tasks;
        _assignments = assignments;
        _sessions = sessions;
        _calendarEvents = calendarEvents;
        _statuses = statuses;
    }

    public async Task<Result<IReadOnlyList<WorkTaskResponse>>> Handle(GetProjectTasksQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<WorkTaskResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<WorkTaskResponse>>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<WorkTaskResponse>>.Forbidden("No employee record for the current user.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<IReadOnlyList<WorkTaskResponse>>.NotFound("Project not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");
        var accessibleObjectiveIds = hasReadPermission
            ? null
            : (await _members.GetActiveObjectiveIdsForEmployeeInProjectAsync(tenantId, project.Id, callerEmployeeId.Value, ct)).ToHashSet();

        var allItems = await _tasks.GetByProjectAsync(tenantId, project.Id, ct);
        if (accessibleObjectiveIds is not null)
            allItems = allItems.Where(t => accessibleObjectiveIds.Contains(t.ObjectiveId)).ToList();

        var statuses = await _statuses.GetProjectTemplateAsync(tenantId, project.Id, ct);
        var completingStatusIds = statuses.Where(status => status.MarksTaskComplete).Select(status => status.Id).ToHashSet();
        var subtasksByParentId = allItems
            .Where(task => task.ParentTaskId is not null)
            .GroupBy(task => task.ParentTaskId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        var items = allItems.Where(task => task.ParentTaskId is null).ToList();

        var assignments = await _assignments.GetByTaskIdsAsync(items.Select(t => t.Id).ToList(), ct);
        var assigneesByTaskId = assignments
            .GroupBy(a => a.TaskId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(a => a.EmployeeId).ToList());

        // A subtask can be assigned to someone other than the parent's assignee. Fold those
        // employee ids into a per-parent set so a person can find the parent task on the
        // Board/Backlog under an assignee filter even when they're only assigned to a subtask
        // of it (subtasks themselves never appear as independent top-level cards).
        var subtaskIds = subtasksByParentId.Values.SelectMany(list => list.Select(s => s.Id)).ToList();
        var subtaskAssignments = await _assignments.GetByTaskIdsAsync(subtaskIds, ct);
        var subtaskAssigneesBySubtaskId = subtaskAssignments
            .GroupBy(a => a.TaskId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.EmployeeId).ToList());
        var subtaskAssigneesByParentId = subtasksByParentId.ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<Guid>)group.Value
                .SelectMany(subtask => subtaskAssigneesBySubtaskId.GetValueOrDefault(subtask.Id, new List<Guid>()))
                .Distinct()
                .ToList());

        // Coverage-free identity resolution (same reasoning as GetObjectiveMembersQueryHandler:
        // management coverage is a People-module reporting-chain concept unrelated to WM task
        // assignment, so GET /employees/{id} 403ing for most assignees must not block the board
        // from showing their name/avatar). Signed once per distinct assignee across the whole
        // response rather than per task, since the same person is often assigned to several tasks.
        var distinctAssigneeIds = assigneesByTaskId.Values.SelectMany(ids => ids).Distinct().ToList();
        var identitiesByEmployeeId = await _identity.ResolveIdentitiesByEmployeeIdAsync(tenantId, distinctAssigneeIds, ct);
        var assigneeIdentityByEmployeeId = new Dictionary<Guid, TaskAssigneeIdentityDto>();
        foreach (var employeeId in distinctAssigneeIds)
        {
            if (!identitiesByEmployeeId.TryGetValue(employeeId, out var identity))
            {
                assigneeIdentityByEmployeeId[employeeId] = new TaskAssigneeIdentityDto(employeeId, "Unknown employee", null);
                continue;
            }

            string? avatarUrl = null;
            if (identity.AvatarFileId is { } avatarFileId)
            {
                var urlResult = await _fileStorage.GetSignedUrlAsync(tenantId, avatarFileId, AvatarUrlExpiry, ct);
                avatarUrl = urlResult.IsSuccess ? urlResult.Value : null;
            }
            assigneeIdentityByEmployeeId[employeeId] = new TaskAssigneeIdentityDto(employeeId, identity.Name, avatarUrl);
        }

        // People filter (spec §6.2): keep only tasks assigned to one of the requested employees.
        // A task also matches if one of its subtasks is assigned to a requested employee, so the
        // parent still surfaces under an assignee filter even when only a subtask carries the
        // assignment.
        if (request.AssigneeEmployeeIds is { Count: > 0 } wantedAssignees)
        {
            var wantedSet = wantedAssignees.ToHashSet();
            items = items
                .Where(t =>
                    assigneesByTaskId.GetValueOrDefault(t.Id, Array.Empty<Guid>()).Any(wantedSet.Contains) ||
                    subtaskAssigneesByParentId.GetValueOrDefault(t.Id, Array.Empty<Guid>()).Any(wantedSet.Contains))
                .ToList();
        }

        var openSessions = await _sessions.GetOpenSessionsForTasksAsync(
            tenantId, items.Select(task => task.Id).ToList(), ct);
        var totalLoggedMinutes = await _sessions.GetTotalClosedSessionMinutesForTasksAsync(
            tenantId, items.Select(task => task.Id).ToList(), ct);

        var eventLinkByTaskId = (await _calendarEvents.ListActiveTaskLinksForTasksAsync(
                tenantId, items.Select(task => task.Id).ToList(), ct))
            .GroupBy(l => l.TaskId)
            .ToDictionary(g => g.Key, g => g.First());

        var responses = items.Select(t => new WorkTaskResponse(
            t.Id, t.ObjectiveId, t.ShortId, t.Title, t.Description, t.CategoryId, t.StatusId,
            t.Priority, t.StoryPoints, t.DueDate, t.EstimatedHours, t.CompletedHours, t.ProgressPercent, t.SprintId,
            assigneesByTaskId.GetValueOrDefault(t.Id, Array.Empty<Guid>()),
            openSessions.TryGetValue(t.Id, out var openSession) ? openSession.EmployeeId : (Guid?)null,
            openSession?.ClockInAt,
            totalLoggedMinutes.GetValueOrDefault(t.Id, 0),
            eventLinkByTaskId.TryGetValue(t.Id, out var eventLink) ? eventLink.CalendarEventId : (Guid?)null,
            eventLink?.EventName,
            Attachments: null,
            Assignees: assigneesByTaskId.GetValueOrDefault(t.Id, Array.Empty<Guid>())
                .Select(employeeId => assigneeIdentityByEmployeeId[employeeId]).ToList(),
            ParentTaskId: t.ParentTaskId,
            SubtaskTotalCount: subtasksByParentId.GetValueOrDefault(t.Id)?.Count ?? 0,
            SubtaskCompletedCount: subtasksByParentId.GetValueOrDefault(t.Id)?.Count(subtask => completingStatusIds.Contains(subtask.StatusId)) ?? 0,
            SubtaskAssigneeEmployeeIds: subtaskAssigneesByParentId.GetValueOrDefault(t.Id, Array.Empty<Guid>()))).ToList();

        return Result<IReadOnlyList<WorkTaskResponse>>.Success(responses);
    }
}
