using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public sealed class TaskAccessResolver : ITaskAccessResolver
{
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IPermissionResolver _permissionResolver;

    public TaskAccessResolver(
        ICallerIdentityResolver identity, IWorkTaskRepository tasks, IProjectRepository projects,
        IProjectMemberRepository members, IPermissionResolver permissionResolver,
        IWorkHierarchyService hierarchy)
    {
        _identity = identity;
        _tasks = tasks;
        _projects = projects;
        _members = members;
        _hierarchy = hierarchy;
        _permissionResolver = permissionResolver;
    }

    public async Task<Result<TaskAccessContext>> ResolveViewableTaskAsync(
        Guid tenantId, Guid userId, Guid taskId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return Result<TaskAccessContext>.Forbidden("Tenant context missing.");

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<TaskAccessContext>.Forbidden("No employee record for the current user.");

        var task = await _tasks.GetByIdForTenantAsync(tenantId, taskId, ct);
        if (task is null)
            return Result<TaskAccessContext>.NotFound("Task not found.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, task.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<TaskAccessContext>.NotFound("Task not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("*");

        if (task.VisibilityScope == WorkTaskVisibilityScopes.Assignees)
        {
            if (hasReadPermission || task.CreatedById == userId || project.LeadId == callerEmployeeId.Value
                || await _tasks.IsAssignedToEmployeeAsync(task.Id, callerEmployeeId.Value, ct))
                return Result<TaskAccessContext>.Success(new TaskAccessContext(task, callerEmployeeId.Value));

            return Result<TaskAccessContext>.NotFound("Task not found.");
        }

        if (!hasReadPermission)
        {
            var accessibleObjectiveIds =
                (await _hierarchy.LoadTreeAsync(tenantId, project.Id, ct)).AtOrBelow(
                    await _members.GetActiveObjectiveIdsForEmployeeInProjectAsync(tenantId, project.Id, callerEmployeeId.Value, ct));
            if (!accessibleObjectiveIds.Contains(task.ObjectiveId))
                return Result<TaskAccessContext>.NotFound("Task not found.");
        }

        return Result<TaskAccessContext>.Success(new TaskAccessContext(task, callerEmployeeId.Value));
    }

    public async Task<IReadOnlyList<WorkTask>> FilterViewableTasksAsync(
        Guid tenantId, Guid userId, Guid callerEmployeeId,
        IReadOnlyList<WorkTask> tasks, CancellationToken ct = default)
    {
        if (tasks.Count == 0)
            return tasks;

        var privateTasks = tasks
            .Where(task => task.VisibilityScope == WorkTaskVisibilityScopes.Assignees)
            .ToList();
        if (privateTasks.Count == 0)
            return tasks;

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        if (permissions.Contains("*"))
            return tasks;

        var assignments = await _tasks.GetAssignedEmployeeIdsByTaskIdsAsync(
            privateTasks.Select(task => task.Id).ToList(), ct);
        var projectLeads = new Dictionary<Guid, Guid>();
        foreach (var projectId in privateTasks.Select(task => task.ProjectId).Distinct())
        {
            var project = await _projects.GetByIdForTenantAsync(tenantId, projectId, ct);
            if (project is not null)
                projectLeads[projectId] = project.LeadId;
        }

        return tasks.Where(task =>
            task.VisibilityScope != WorkTaskVisibilityScopes.Assignees
            || task.CreatedById == userId
            || projectLeads.GetValueOrDefault(task.ProjectId) == callerEmployeeId
            || assignments.GetValueOrDefault(task.Id, Array.Empty<Guid>()).Contains(callerEmployeeId)).ToList();
    }
}
