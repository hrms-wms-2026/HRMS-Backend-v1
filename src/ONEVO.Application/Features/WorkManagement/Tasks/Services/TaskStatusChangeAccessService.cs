using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

/// <summary>
/// The caller's standing over a project's status template. Approver = root (default) module owner
/// only; anyone else who owns or is an active member of any module in the project may file a change
/// request.
/// </summary>
public sealed record TaskStatusChangeAccess(Objective RootObjective, bool CanEditDirectly, bool CanRequest);

public interface ITaskStatusChangeAccessService
{
    /// <summary>Null when the project has no root (default) module.</summary>
    Task<TaskStatusChangeAccess?> ResolveAsync(Guid tenantId, Guid projectId, Guid employeeId, CancellationToken ct = default);
}

public sealed class TaskStatusChangeAccessService : ITaskStatusChangeAccessService
{
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberRepository _members;

    public TaskStatusChangeAccessService(IObjectiveRepository objectives, IProjectMemberRepository members)
    {
        _objectives = objectives;
        _members = members;
    }

    public async Task<TaskStatusChangeAccess?> ResolveAsync(Guid tenantId, Guid projectId, Guid employeeId, CancellationToken ct = default)
    {
        var root = await _objectives.GetDefaultByProjectIdAsync(tenantId, projectId, ct);
        if (root is null)
            return null;

        // Root Module has no parent, so "at or above the root position" == "owns the root".
        // Root members are not approvers any more (user decision 2026-09-28): they request.
        if (root.OwnerId == employeeId)
            return new TaskStatusChangeAccess(root, CanEditDirectly: true, CanRequest: false);

        // A module owner is not guaranteed a ProjectMember row, so check ownership separately.
        var canRequest = await _members.HasActiveMembershipAsync(tenantId, projectId, employeeId, ct);
        if (!canRequest)
        {
            var modules = await _objectives.GetAllByProjectIdAsync(tenantId, projectId, ct);
            canRequest = modules.Any(m => m.IsActive && m.OwnerId == employeeId);
        }

        return new TaskStatusChangeAccess(root, CanEditDirectly: false, CanRequest: canRequest);
    }
}
