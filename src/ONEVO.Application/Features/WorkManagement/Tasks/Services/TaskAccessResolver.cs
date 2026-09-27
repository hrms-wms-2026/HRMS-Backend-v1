using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

public sealed class TaskAccessResolver : ITaskAccessResolver
{
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkTaskRepository _tasks;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;

    public TaskAccessResolver(
        ICallerIdentityResolver identity, IWorkTaskRepository tasks, IProjectRepository projects,
        IProjectMemberRepository members, IPermissionResolver permissionResolver)
    {
        _identity = identity;
        _tasks = tasks;
        _projects = projects;
        _members = members;
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
        if (!hasReadPermission)
        {
            var accessibleObjectiveIds =
                (await _members.GetActiveObjectiveIdsForEmployeeInProjectAsync(tenantId, project.Id, callerEmployeeId.Value, ct))
                .ToHashSet();
            if (!accessibleObjectiveIds.Contains(task.ObjectiveId))
                return Result<TaskAccessContext>.NotFound("Task not found.");
        }

        return Result<TaskAccessContext>.Success(new TaskAccessContext(task, callerEmployeeId.Value));
    }
}
