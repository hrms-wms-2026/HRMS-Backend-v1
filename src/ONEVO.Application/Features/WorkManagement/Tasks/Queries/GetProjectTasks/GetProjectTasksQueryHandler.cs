using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetProjectTasks;

public sealed class GetProjectTasksQueryHandler : IRequestHandler<GetProjectTasksQuery, Result<IReadOnlyList<WorkTaskResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly ITaskClockingSessionRepository _sessions;

    public GetProjectTasksQueryHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IProjectRepository projects,
        IProjectMemberRepository members,
        IPermissionResolver permissionResolver,
        IWorkTaskRepository tasks,
        ITaskAssignmentRepository assignments,
        ITaskClockingSessionRepository sessions)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _members = members;
        _permissionResolver = permissionResolver;
        _tasks = tasks;
        _assignments = assignments;
        _sessions = sessions;
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

        var items = await _tasks.GetByProjectAsync(tenantId, project.Id, ct);
        if (accessibleObjectiveIds is not null)
            items = items.Where(t => accessibleObjectiveIds.Contains(t.ObjectiveId)).ToList();

        var assignments = await _assignments.GetByTaskIdsAsync(items.Select(t => t.Id).ToList(), ct);
        var assigneesByTaskId = assignments
            .GroupBy(a => a.TaskId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(a => a.EmployeeId).ToList());

        var openSessions = await _sessions.GetOpenSessionsForTasksAsync(
            tenantId, items.Select(task => task.Id).ToList(), ct);
        var totalLoggedMinutes = await _sessions.GetTotalClosedSessionMinutesForTasksAsync(
            tenantId, items.Select(task => task.Id).ToList(), ct);

        var responses = items.Select(t => new WorkTaskResponse(
            t.Id, t.ObjectiveId, t.ShortId, t.Title, t.Description, t.CategoryId, t.StatusId,
            t.Priority, t.StoryPoints, t.DueDate, t.EstimatedHours, t.CompletedHours, t.ProgressPercent, t.SprintId,
            assigneesByTaskId.GetValueOrDefault(t.Id, Array.Empty<Guid>()),
            openSessions.TryGetValue(t.Id, out var openSession) ? openSession.EmployeeId : (Guid?)null,
            openSession?.ClockInAt,
            totalLoggedMinutes.GetValueOrDefault(t.Id, 0))).ToList();

        return Result<IReadOnlyList<WorkTaskResponse>>.Success(responses);
    }
}
