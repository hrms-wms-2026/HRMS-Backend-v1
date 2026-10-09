using ONEVO.Application.Features.WorkManagement.Objectives.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Services;

public sealed record LedObjectiveRow(Guid Id, Guid ProjectId, Guid? ParentObjectiveId, string Title, bool IsDefault, DateOnly EndDate);

public sealed record LedHeadModule(
    Guid ObjectiveId, Guid ProjectId, string Title, bool IsRootModule, DateOnly EndDate, IReadOnlySet<Guid> SubtreeObjectiveIds);

public sealed record LedWorkScope(IReadOnlyList<LedHeadModule> HeadModules, IReadOnlyList<Guid> DroppedOwnedObjectiveIds)
{
    public static readonly LedWorkScope Empty = new(Array.Empty<LedHeadModule>(), Array.Empty<Guid>());

    public IReadOnlySet<Guid> AllObjectiveIds { get; } =
        HeadModules.SelectMany(head => head.SubtreeObjectiveIds).ToHashSet();
}

/// <summary>"Work I Lead" (My Team spec §8.3.2): the modules the caller effectively OWNS - owner of
/// the module or of any ancestor module, the same test MilestoneMembershipCoordinator.
/// IsEffectiveOwnerAsync and SprintAccessService use - evaluated in bulk. Heads are owned modules
/// with no owned ancestor, so nested ownership never double counts.
///
/// Membership is used ONLY to restrict (step 7, review clarification 2026-09-30): an owned subtree
/// the caller cannot open through TaskAccessResolver's membership rule is dropped rather than
/// leaked, and never auto-granted just because the employee is marked as owner. This is a
/// deliberate data-integrity safety net, not the normal case - an owner normally gets a membership
/// row at module creation or transfer (CreateProject, CreateObjective, TransferObjectiveHead), but
/// TaskStatusChangeAccessService's own doc comment notes "a module owner is not guaranteed a
/// ProjectMember row", and demo seeders set OwnerId directly without a membership row. Plain
/// membership never qualifies anyone as a lead.</summary>
public static class LedWorkScopeBuilder
{
    public static LedWorkScope Build(
        IReadOnlyCollection<Guid> ownedObjectiveIds,
        IReadOnlyCollection<LedObjectiveRow> activeTree,
        IReadOnlyCollection<Guid> membershipObjectiveIds)
    {
        if (ownedObjectiveIds.Count == 0)
            return LedWorkScope.Empty;

        var owned = ownedObjectiveIds.ToHashSet();
        var nodes = activeTree.Select(row => new ObjectiveTreeNode(row.Id, row.ParentObjectiveId)).ToList();
        var parentOf = ObjectiveTreeExpander.ParentMap(nodes);
        var rowsById = activeTree.ToDictionary(row => row.Id);
        var accessible = ObjectiveTreeExpander.ExpandWithDescendants(membershipObjectiveIds, nodes);

        var heads = new List<LedHeadModule>();
        var dropped = new List<Guid>();
        foreach (var objectiveId in owned.OrderBy(id => id))
        {
            if (!rowsById.TryGetValue(objectiveId, out var row) || HasOwnedAncestor(objectiveId, owned, parentOf))
                continue;

            if (!accessible.Contains(objectiveId))
            {
                dropped.Add(objectiveId);
                continue;
            }

            var subtree = ObjectiveTreeExpander.ExpandWithDescendants(new[] { objectiveId }, nodes);
            subtree.IntersectWith(accessible);
            heads.Add(new LedHeadModule(row.Id, row.ProjectId, row.Title, row.IsDefault, row.EndDate, subtree));
        }

        return heads.Count == 0 && dropped.Count == 0 ? LedWorkScope.Empty : new LedWorkScope(heads, dropped);
    }

    private static bool HasOwnedAncestor(Guid objectiveId, IReadOnlySet<Guid> owned, IReadOnlyDictionary<Guid, Guid?> parentOf)
    {
        var visited = new HashSet<Guid> { objectiveId };
        var cursor = parentOf.GetValueOrDefault(objectiveId);
        while (cursor is Guid parent && visited.Add(parent))
        {
            if (owned.Contains(parent))
                return true;
            cursor = parentOf.GetValueOrDefault(parent);
        }
        return false;
    }
}
