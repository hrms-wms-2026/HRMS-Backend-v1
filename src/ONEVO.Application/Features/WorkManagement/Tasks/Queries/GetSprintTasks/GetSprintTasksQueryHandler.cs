using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetSprintTasks;

public class GetSprintTasksQueryHandler : IRequestHandler<GetSprintTasksQuery, Result<IReadOnlyList<WorkTaskResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ISprintRepository _sprints;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskAssignmentRepository _assignments;

    public GetSprintTasksQueryHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        ISprintRepository sprints,
        IProjectMemberRepository members,
        IPermissionResolver permissionResolver,
        IWorkTaskRepository tasks,
        ITaskAssignmentRepository assignments)
    {
        _currentUser = currentUser;
        _identity = identity;
        _sprints = sprints;
        _members = members;
        _permissionResolver = permissionResolver;
        _tasks = tasks;
        _assignments = assignments;
    }

    public async Task<Result<IReadOnlyList<WorkTaskResponse>>> Handle(GetSprintTasksQuery request, CancellationToken ct)
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

        var sprint = await _sprints.GetByIdForTenantAsync(tenantId, request.SprintId, ct);
        if (sprint is null)
            return Result<IReadOnlyList<WorkTaskResponse>>.NotFound("Sprint not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("*");
        if (!hasReadPermission && !await _members.HasActiveMembershipAsync(tenantId, sprint.ProjectId, callerEmployeeId.Value, ct))
            return Result<IReadOnlyList<WorkTaskResponse>>.Forbidden("You do not have access to this project.");

        var items = await _tasks.GetBySprintIdAsync(tenantId, request.SprintId, ct);

        var assignments = await _assignments.GetByTaskIdsAsync(items.Select(t => t.Id).ToList(), ct);
        var assigneesByTaskId = assignments
            .GroupBy(a => a.TaskId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(a => a.EmployeeId).ToList());

        var responses = items.Select(t => new WorkTaskResponse(
            t.Id, t.ObjectiveId, t.ShortId, t.Title, t.Description, t.CategoryId, t.StatusId,
            t.Priority, t.StoryPoints, t.DueDate, t.EstimatedHours, t.CompletedHours, t.ProgressPercent, t.SprintId,
            assigneesByTaskId.GetValueOrDefault(t.Id, Array.Empty<Guid>()), CreatedAt: t.CreatedAt)).ToList();

        return Result<IReadOnlyList<WorkTaskResponse>>.Success(responses);
    }
}
