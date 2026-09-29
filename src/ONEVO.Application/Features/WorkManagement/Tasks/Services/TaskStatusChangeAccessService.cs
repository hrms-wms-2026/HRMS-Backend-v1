using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Services;

/// <summary>
/// The caller's standing over a project's status template. Approvers (root/default module owner or
/// an active root member - the same gate the direct status handlers use) edit directly; anyone else
/// who owns or is an active member of any module in the project may only file a change request.
/// </summary>
public sealed record TaskStatusChangeAccess(Objective RootObjective, bool CanEditDirectly, bool CanRequest);

public interface ITaskStatusChangeAccessService
{
    /// <summary>Null when the project has no root (default) module.</summary>
    Task<TaskStatusChangeAccess?> ResolveAsync(Guid tenantId, Guid projectId, Guid employeeId, CancellationToken ct = default);

    /// <summary>The root module owner plus its active members - everyone who may decide a request.</summary>
    Task<IReadOnlyList<Guid>> ListApproverEmployeeIdsAsync(Guid tenantId, Objective rootObjective, CancellationToken ct = default);
}

public sealed class TaskStatusChangeAccessService : ITaskStatusChangeAccessService
{
    private readonly IObjectiveRepository _objectives;
    private readonly IProjectMemberRepository _members;
    private readonly IMilestoneMembershipCoordinator _membership;

    public TaskStatusChangeAccessService(
        IObjectiveRepository objectives, IProjectMemberRepository members, IMilestoneMembershipCoordinator membership)
    {
        _objectives = objectives;
        _members = members;
        _membership = membership;
    }

    public async Task<TaskStatusChangeAccess?> ResolveAsync(Guid tenantId, Guid projectId, Guid employeeId, CancellationToken ct = default)
    {
        var root = await _objectives.GetDefaultByProjectIdAsync(tenantId, projectId, ct);
        if (root is null)
            return null;

        if (await _membership.IsEffectiveManagerAsync(tenantId, root.Id, employeeId, ct))
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

    public async Task<IReadOnlyList<Guid>> ListApproverEmployeeIdsAsync(Guid tenantId, Objective rootObjective, CancellationToken ct = default)
    {
        var members = await _members.ListActiveForObjectiveAsync(tenantId, rootObjective.Id, ct);
        return members.Select(m => m.EmployeeId).Prepend(rootObjective.OwnerId).Distinct().ToList();
    }
}
