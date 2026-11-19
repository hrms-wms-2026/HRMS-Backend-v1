namespace ONEVO.Application.Features.WorkManagement.Hierarchy;

/// <summary>
/// Work Management's hierarchy service: the project Module tree plus "who currently holds this
/// position". Modelled on CoreHr's IEmployeeAuthorityResolver (single interface, fixed walk order,
/// fails closed with null) but over the project tree instead of the org chart.
/// </summary>
public interface IWorkHierarchyService
{
    Task<ProjectModuleTree> LoadTreeAsync(Guid tenantId, Guid projectId, CancellationToken ct = default);

    /// <summary>Walks the position Module and then its ancestors, and returns the first owner who is
    /// an active employee and is not excludingEmployeeId. Null if nobody qualifies.</summary>
    Task<Guid?> FindActiveHolderAsync(
        Guid tenantId, ProjectModuleTree tree, Guid positionModuleId, Guid excludingEmployeeId, CancellationToken ct = default);
}
