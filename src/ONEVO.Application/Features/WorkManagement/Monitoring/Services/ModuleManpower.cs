using ONEVO.Application.Features.WorkManagement.Hierarchy;

namespace ONEVO.Application.Features.WorkManagement.Monitoring.Services;

/// <summary>
/// The manpower behind a Module's allocated hours. A Module's allocation is split across all its
/// sub-Modules, so the people who can produce it are everyone at or below the Module: the active
/// members and the owner of the Module itself and of every descendant, each counted once.
/// </summary>
public static class ModuleManpower
{
    /// <param name="membersByModule">Active member employee ids per Module id.</param>
    /// <param name="extraEmployeeIds">People not stored yet, e.g. members picked on the create form.</param>
    public static int Count(
        ProjectModuleTree tree, Guid moduleId, IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> membersByModule,
        IEnumerable<Guid>? extraEmployeeIds = null)
    {
        var people = new HashSet<Guid>(extraEmployeeIds ?? []);
        foreach (var id in tree.AtOrBelow([moduleId]))
        {
            if (tree.Get(id) is { } module && module.IsActive && !module.IsDeleted)
                people.Add(module.OwnerId);
            else if (tree.Get(id) is not null)
                continue;   // an inactive sub-Module's people no longer work on this allocation
            if (membersByModule.TryGetValue(id, out var members))
                people.UnionWith(members);
        }
        people.Remove(Guid.Empty);
        return people.Count;
    }
}
