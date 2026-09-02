using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskById;

public sealed class GetTaskByIdQueryHandler : IRequestHandler<GetTaskByIdQuery, Result<WorkTaskResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly ITaskAssignmentRepository _assignments;
    private readonly ITaskClockingSessionRepository _sessions;

    public GetTaskByIdQueryHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IWorkTaskRepository tasks,
        IProjectRepository projects,
        IProjectMemberRepository members,
        IPermissionResolver permissionResolver,
        ITaskAssignmentRepository assignments,
        ITaskClockingSessionRepository sessions)
    {
        _currentUser = currentUser;
        _identity = identity;
        _tasks = tasks;
        _projects = projects;
        _members = members;
        _permissionResolver = permissionResolver;
        _assignments = assignments;
        _sessions = sessions;
    }

    public async Task<Result<WorkTaskResponse>> Handle(GetTaskByIdQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<WorkTaskResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<WorkTaskResponse>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<WorkTaskResponse>.Forbidden("No employee record for the current user.");

        var task = await _tasks.GetByIdForTenantAsync(tenantId, request.TaskId, ct);
        if (task is null)
            return Result<WorkTaskResponse>.NotFound("Task not found.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, task.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<WorkTaskResponse>.NotFound("Task not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");
        if (!hasReadPermission)
        {
            var accessibleObjectiveIds =
                (await _members.GetActiveObjectiveIdsForEmployeeInProjectAsync(tenantId, project.Id, callerEmployeeId.Value, ct))
                .ToHashSet();
            if (!accessibleObjectiveIds.Contains(task.ObjectiveId))
                return Result<WorkTaskResponse>.NotFound("Task not found.");
        }

        var assignments = await _assignments.GetByTaskIdsAsync(new[] { task.Id }, ct);
        var assigneeIds = (IReadOnlyList<Guid>)assignments.Select(a => a.EmployeeId).ToList();

        var openSessions = await _sessions.GetOpenSessionsForTasksAsync(tenantId, new[] { task.Id }, ct);
        var totalLoggedMinutes = await _sessions.GetTotalClosedSessionMinutesForTasksAsync(tenantId, new[] { task.Id }, ct);

        var response = new WorkTaskResponse(
            task.Id, task.ObjectiveId, task.ShortId, task.Title, task.Description, task.CategoryId, task.StatusId,
            task.Priority, task.StoryPoints, task.DueDate, task.EstimatedHours, task.CompletedHours, task.ProgressPercent, task.SprintId,
            assigneeIds,
            openSessions.TryGetValue(task.Id, out var openSession) ? openSession.EmployeeId : (Guid?)null,
            openSession?.ClockInAt,
            totalLoggedMinutes.GetValueOrDefault(task.Id, 0));

        return Result<WorkTaskResponse>.Success(response);
    }
}
