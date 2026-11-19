namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

public readonly record struct ObjectiveTreeNode(Guid Id, Guid? ParentObjectiveId);

/// <summary>The one "a module grants its whole sub-module subtree" walk, shared by task visibility
/// (EfProjectMemberRepository.GetActiveObjectiveIdsForEmployeeInProjectAsync) and My Team's Work I
/// Lead scope (My Team spec §8.3.2). Pure: callers load the active objective tree once and pass it
/// in.</summary>
public static class ObjectiveTreeExpander
{
    public static HashSet<Guid> ExpandWithDescendants(IEnumerable<Guid> roots, IReadOnlyCollection<ObjectiveTreeNode> activeNodes)
    {
        var result = new HashSet<Guid>(roots);
        if (result.Count == 0)
            return result;

        var childrenByParentId = activeNodes
            .Where(node => node.ParentObjectiveId is not null)
            .GroupBy(node => node.ParentObjectiveId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(node => node.Id).ToList());

        var pending = new Queue<Guid>(result);
        while (pending.Count > 0)
        {
            if (!childrenByParentId.TryGetValue(pending.Dequeue(), out var childIds))
                continue;
            foreach (var childId in childIds)
            {
                if (result.Add(childId))
                    pending.Enqueue(childId);
            }
        }

        return result;
    }

    public static IReadOnlyDictionary<Guid, Guid?> ParentMap(IReadOnlyCollection<ObjectiveTreeNode> nodes)
        => nodes.ToDictionary(node => node.Id, node => node.ParentObjectiveId);
}
