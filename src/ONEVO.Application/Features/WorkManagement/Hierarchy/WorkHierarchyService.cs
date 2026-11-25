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

    public async Task<IReadOnlyDictionary<Guid, Guid?>> FindActiveHoldersAsync(
        Guid tenantId, ProjectModuleTree tree, IReadOnlyCollection<Guid> positionModuleIds, CancellationToken ct = default)
    {
        var chains = positionModuleIds.Distinct().ToDictionary(id => id, id => tree.AncestorChain(id));
        var active = await _membership.GetActiveAssigneeIdsAsync(
            tenantId, chains.Values.SelectMany(chain => chain.Select(m => m.OwnerId)).Distinct().ToList(), ct);
        return chains.ToDictionary(c => c.Key, c => c.Value.Select(m => (Guid?)m.OwnerId).FirstOrDefault(o => active.Contains(o!.Value)));
    }
}
