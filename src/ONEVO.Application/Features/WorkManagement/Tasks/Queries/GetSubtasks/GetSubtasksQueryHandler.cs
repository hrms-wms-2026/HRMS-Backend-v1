using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetSubtasks;

public sealed class GetSubtasksQueryHandler : IRequestHandler<GetSubtasksQuery, Result<IReadOnlyList<WorkTaskResponse>>>
{
    private static readonly TimeSpan AvatarUrlExpiry = TimeSpan.FromMinutes(15);
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IFileStorageService _fileStorage;
    private readonly IWorkTaskRepository _tasks;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly ITaskClockingSessionRepository _sessions;

    public GetSubtasksQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IFileStorageService fileStorage,
        IWorkTaskRepository tasks, IProjectRepository projects, IProjectMemberRepository members,
        IPermissionResolver permissionResolver, ITaskAssignmentRepository assignments,
        ITaskClockingSessionRepository sessions)
    {
        _currentUser = currentUser;
        _identity = identity;
        _fileStorage = fileStorage;
        _tasks = tasks;
        _projects = projects;
        _members = members;
        _permissionResolver = permissionResolver;
        _assignments = assignments;
        _sessions = sessions;
    }

    public async Task<Result<IReadOnlyList<WorkTaskResponse>>> Handle(GetSubtasksQuery request, CancellationToken ct)
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

        var parent = await _tasks.GetByIdForTenantAsync(tenantId, request.ParentTaskId, ct);
        if (parent is null)
            return Result<IReadOnlyList<WorkTaskResponse>>.NotFound("Task not found.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, parent.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<IReadOnlyList<WorkTaskResponse>>.NotFound("Task not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        if (!permissions.Contains("projects:read") && !permissions.Contains("*"))
        {
            var accessibleObjectiveIds =
                (await _members.GetActiveObjectiveIdsForEmployeeInProjectAsync(tenantId, project.Id, callerEmployeeId.Value, ct))
                .ToHashSet();
            if (!accessibleObjectiveIds.Contains(parent.ObjectiveId))
                return Result<IReadOnlyList<WorkTaskResponse>>.NotFound("Task not found.");
        }

        var children = await _tasks.GetByParentTaskIdAsync(tenantId, parent.Id, ct);
        if (children.Count == 0)
            return Result<IReadOnlyList<WorkTaskResponse>>.Success(Array.Empty<WorkTaskResponse>());

        var assignments = await _assignments.GetByTaskIdsAsync(children.Select(child => child.Id).ToList(), ct);
        var assigneesByTaskId = assignments
            .GroupBy(assignment => assignment.TaskId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Guid>)group.Select(a => a.EmployeeId).ToList());

        var distinctAssigneeIds = assigneesByTaskId.Values.SelectMany(ids => ids).Distinct().ToList();
        var identitiesByEmployeeId = await _identity.ResolveIdentitiesByEmployeeIdAsync(tenantId, distinctAssigneeIds, ct);
        var assigneeIdentityByEmployeeId = new Dictionary<Guid, TaskAssigneeIdentityDto>();
        foreach (var employeeId in distinctAssigneeIds)
        {
            if (!identitiesByEmployeeId.TryGetValue(employeeId, out var employeeIdentity))
            {
                assigneeIdentityByEmployeeId[employeeId] = new TaskAssigneeIdentityDto(employeeId, "Unknown employee", null);
                continue;
            }

            string? avatarUrl = null;
            if (employeeIdentity.AvatarFileId is { } avatarFileId)
            {
                var urlResult = await _fileStorage.GetSignedUrlAsync(tenantId, avatarFileId, AvatarUrlExpiry, ct);
                avatarUrl = urlResult.IsSuccess ? urlResult.Value : null;
            }
            assigneeIdentityByEmployeeId[employeeId] = new TaskAssigneeIdentityDto(employeeId, employeeIdentity.Name, avatarUrl);
        }

        var childIds = children.Select(child => child.Id).ToList();
        var openSessions = await _sessions.GetOpenSessionsForTasksAsync(tenantId, childIds, ct);
        var totalLoggedMinutes = await _sessions.GetTotalClosedSessionMinutesForTasksAsync(tenantId, childIds, ct);

        var responses = children.Select(task => new WorkTaskResponse(
            task.Id, task.ObjectiveId, task.ShortId, task.Title, task.Description, task.CategoryId, task.StatusId,
            task.Priority, task.StoryPoints, task.DueDate, task.EstimatedHours, task.CompletedHours, task.ProgressPercent, task.SprintId,
            AssigneeEmployeeIds: assigneesByTaskId.GetValueOrDefault(task.Id, Array.Empty<Guid>()),
            OpenClockSessionEmployeeId: openSessions.TryGetValue(task.Id, out var openSession) ? openSession.EmployeeId : (Guid?)null,
            OpenClockSessionClockInAt: openSession?.ClockInAt,
            TotalLoggedMinutes: totalLoggedMinutes.GetValueOrDefault(task.Id, 0),
            Assignees: assigneesByTaskId.GetValueOrDefault(task.Id, Array.Empty<Guid>())
                .Select(employeeId => assigneeIdentityByEmployeeId[employeeId]).ToList(),
            ParentTaskId: task.ParentTaskId, CreatedAt: task.CreatedAt)).ToList();

        return Result<IReadOnlyList<WorkTaskResponse>>.Success(responses);
    }
}
