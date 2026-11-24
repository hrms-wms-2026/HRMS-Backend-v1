using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;

namespace ONEVO.Application.Features.WorkManagement.Hierarchy;

public sealed class WorkHierarchyService : IWorkHierarchyService
{
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;

    public WorkHierarchyService(IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership)
    {
        _objectives = objectives;
        _membership = membership;
    }

    public async Task<ProjectModuleTree> LoadTreeAsync(Guid tenantId, Guid projectId, CancellationToken ct = default)
        => new(await _objectives.GetAllByProjectIdAsync(tenantId, projectId, ct));

    public async Task<IReadOnlyDictionary<Guid, ProjectModuleTree>> LoadTreesAsync(
        Guid tenantId, IReadOnlyCollection<Guid> projectIds, CancellationToken ct = default)
    {
        var modulesByProject = (await _objectives.GetAllByProjectIdsAsync(tenantId, projectIds, ct)).ToLookup(m => m.ProjectId);
        return projectIds.Distinct().ToDictionary(id => id, id => new ProjectModuleTree(modulesByProject[id]));
    }

    public async Task<Guid?> FindActiveHolderAsync(
        Guid tenantId, ProjectModuleTree tree, Guid positionModuleId, Guid excludingEmployeeId, CancellationToken ct = default)
    {
        var checkedOwners = new HashSet<Guid>();
        foreach (var module in tree.AncestorChain(positionModuleId))
        {
            var ownerId = module.OwnerId;
            if (ownerId == excludingEmployeeId || !checkedOwners.Add(ownerId))
                continue;
            if (await _membership.GetActiveAssigneeAsync(tenantId, ownerId, ct) is not null)
                return ownerId;
        }
        return null;
    }
}
