using ONEVO.Application.Features.WorkManagement.Objectives.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Services;

/// <summary>Narrows a caller's led scope by the optional Team Progress detail filters. Never widens
/// it: a projectId or objectiveId outside the scope yields an empty set (the caller then returns an
/// empty page), and an objectiveId is expanded only to its descendants that are themselves in scope.</summary>
public static class LedScopeFilter
{
    public static IReadOnlyCollection<Guid> Resolve(LedWorkScope scope, Guid? projectId, Guid? objectiveId)
    {
        IEnumerable<LedHeadModule> heads = scope.HeadModules;
        if (projectId is Guid project)
            heads = heads.Where(head => head.ProjectId == project);

        if (objectiveId is not Guid objective)
            return heads.SelectMany(head => head.SubtreeObjectiveIds).ToHashSet();

        var owningHead = heads.FirstOrDefault(head => head.SubtreeObjectiveIds.Contains(objective));
        if (owningHead is null)
            return Array.Empty<Guid>();

        var nodes = (owningHead.SubtreeRows ?? [])
            .Select(row => new ObjectiveTreeNode(row.Id, row.ParentObjectiveId))
            .ToList();
        var subtree = ObjectiveTreeExpander.ExpandWithDescendants(new[] { objective }, nodes);
        subtree.IntersectWith(owningHead.SubtreeObjectiveIds);
        return subtree;
    }

    /// <summary>The allocation of the narrowed scope, totalled the way GetLedWorkProgressQueryHandler
    /// and LedHourRollup do: only heads whose project still exists (<paramref name="liveProjectIds"/>,
    /// the handler's projects lookup) count; no filter = the sum of those heads' AllocatedHours
    /// (= led-progress hours.allocatedHours); a projectId keeps that project's heads; an objectiveId
    /// is the allocation of that one Module (a child Module's allocation is already part of its
    /// parent's, so its subtree is never added on top). Out of scope = 0.</summary>
    public static decimal AllocatedHours(
        LedWorkScope scope, IReadOnlySet<Guid> liveProjectIds, Guid? projectId, Guid? objectiveId)
    {
        var heads = scope.HeadModules.Where(head => liveProjectIds.Contains(head.ProjectId));
        if (projectId is Guid project)
            heads = heads.Where(head => head.ProjectId == project);

        if (objectiveId is not Guid objective)
            return heads.Sum(head => head.AllocatedHours);

        var owningHead = heads.FirstOrDefault(head => head.SubtreeObjectiveIds.Contains(objective));
        if (owningHead is null)
            return 0;
        if (owningHead.ObjectiveId == objective)
            return owningHead.AllocatedHours;
        return owningHead.SubtreeRows?.FirstOrDefault(row => row.Id == objective)?.AllocatedHours ?? 0;
    }
}
