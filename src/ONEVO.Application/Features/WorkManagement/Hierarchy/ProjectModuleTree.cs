using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Hierarchy;

/// <summary>
/// Immutable in-memory view of one project's Module (Objective) tree - the single place the
/// "walk up ParentObjectiveId" rule lives. Loaded once per operation by IWorkHierarchyService so
/// every hierarchy question after that is a dictionary walk, not a query per level.
/// </summary>
public sealed class ProjectModuleTree
{
    private readonly IReadOnlyDictionary<Guid, Objective> _byId;

    public ProjectModuleTree(IEnumerable<Objective> modules)
        => _byId = modules.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());

    public Objective? Get(Guid moduleId) => _byId.GetValueOrDefault(moduleId);

    /// <summary>The project's default (top) Module; falls back to any parentless Module.</summary>
    public Objective? Root =>
        _byId.Values.FirstOrDefault(m => m.ParentObjectiveId is null && m.IsDefault)
        ?? _byId.Values.FirstOrDefault(m => m.ParentObjectiveId is null);

    /// <summary>Self first, root last. Stops on missing parents and on cycles in bad data.</summary>
    public IReadOnlyList<Objective> AncestorChain(Guid moduleId)
    {
        var chain = new List<Objective>();
        var seen = new HashSet<Guid>();
        var cursor = Get(moduleId);
        while (cursor is not null && seen.Add(cursor.Id))
        {
            chain.Add(cursor);
            cursor = cursor.ParentObjectiveId is { } parentId ? Get(parentId) : null;
        }
        return chain;
    }

    /// <summary>True when the employee owns the position Module or any of its ancestors.</summary>
    public bool IsAtOrAbove(Guid employeeId, Guid positionModuleId)
        => AncestorChain(positionModuleId).Any(m => m.OwnerId == employeeId);

    /// <summary>The Module owned by this employee that is closest to the root, or null.</summary>
    public Guid? HighestOwnedModuleId(Guid employeeId)
        => _byId.Values
            .Where(m => m.OwnerId == employeeId)
            .Select(m => (m.Id, Depth: AncestorChain(m.Id).Count))
            .OrderBy(x => x.Depth)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefault();
}
