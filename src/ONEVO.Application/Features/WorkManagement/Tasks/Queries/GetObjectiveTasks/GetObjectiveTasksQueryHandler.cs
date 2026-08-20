using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetObjectiveTasks;

public class GetObjectiveTasksQueryHandler : IRequestHandler<GetObjectiveTasksQuery, Result<IReadOnlyList<WorkTaskResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskAssignmentRepository _assignments;

    public GetObjectiveTasksQueryHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        IObjectiveRepository objectives,
        IProjectMemberRepository members,
        IPermissionResolver permissionResolver,
        IWorkTaskRepository tasks,
        ITaskAssignmentRepository assignments)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _members = members;
        _permissionResolver = permissionResolver;
        _tasks = tasks;
        _assignments = assignments;
    }

    public async Task<Result<IReadOnlyList<WorkTaskResponse>>> Handle(GetObjectiveTasksQuery request, CancellationToken ct)
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

        var objective = await _objectives.GetByIdForTenantAsync(tenantId, request.ObjectiveId, ct);
        if (objective is null)
            return Result<IReadOnlyList<WorkTaskResponse>>.NotFound("Objective not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("projects:read") || permissions.Contains("*");

        if (!hasReadPermission)
        {
            var selfAndAncestorIds = new List<Guid> { objective.Id };
            var cursor = objective;
            while (cursor.ParentObjectiveId is not null)
            {
                var ancestor = await _objectives.GetByIdForTenantAsync(tenantId, cursor.ParentObjectiveId.Value, ct);
                if (ancestor is null)
                    break;

                selfAndAncestorIds.Add(ancestor.Id);
                cursor = ancestor;
            }

            var hasAccess = await _members.HasActiveMembershipForAnyObjectiveAsync(tenantId, objective.ProjectId, callerEmployeeId.Value, selfAndAncestorIds, ct);
            if (!hasAccess)
                return Result<IReadOnlyList<WorkTaskResponse>>.Forbidden("You do not have access to this milestone.");
        }

        var items = await _tasks.GetByObjectiveIdAsync(tenantId, request.ObjectiveId, ct);

        var assignments = await _assignments.GetByTaskIdsAsync(items.Select(t => t.Id).ToList(), ct);
        var assigneesByTaskId = assignments
            .GroupBy(a => a.TaskId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(a => a.EmployeeId).ToList());

        var responses = items.Select(t => new WorkTaskResponse(
            t.Id, t.ObjectiveId, t.ShortId, t.Title, t.Description, t.TaskType, t.StatusId,
            t.Priority, t.StoryPoints, t.DueDate, t.EstimatedHours, t.CompletedHours, t.ProgressPercent, t.SprintId,
            assigneesByTaskId.GetValueOrDefault(t.Id, Array.Empty<Guid>()))).ToList();

        return Result<IReadOnlyList<WorkTaskResponse>>.Success(responses);
    }
}
