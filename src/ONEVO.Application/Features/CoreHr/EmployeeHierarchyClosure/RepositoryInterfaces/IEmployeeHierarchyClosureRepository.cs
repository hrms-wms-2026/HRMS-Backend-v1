namespace ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;

public interface IEmployeeHierarchyClosureRepository
{
    /// <summary>
    /// Rebuilds the entire tenant's closure rows from current active PrimaryEmployment
    /// position_assignments and positions.reports_to_position_id. This table is not source
    /// of truth and is safe to delete and rebuild in full per tenant.
    /// </summary>
    Task RebuildAsync(Guid tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetDirectReportEmployeeIdsAsync(
        Guid tenantId, Guid managerEmployeeId, CancellationToken ct = default);

    Task<Guid?> GetDirectManagerEmployeeIdAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetDescendantEmployeeIdsAsync(
        Guid tenantId,
        Guid managerEmployeeId,
        CancellationToken ct = default);

    /// <summary>Distinct transitive descendant employee ids (any depth) of the given ancestor
    /// employee ids, used by IEmployeeAuthorityResolver to expand a covered position's holder(s)
    /// into their full reporting-line subtree for visibility, and to guard approval routing
    /// against ever selecting a subordinate of the subject employee.</summary>
    Task<IReadOnlyList<Guid>> GetDescendantEmployeeIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> ancestorEmployeeIds, CancellationToken ct = default);

    /// <summary>The full upward reporting chain for one employee, ordered nearest-manager-first
    /// (Depth ascending). Used by IEmployeeAuthorityResolver both as the upward-only eligibility
    /// guard for coverage-based approval candidates and as the walk order for reporting-line
    /// fallback.</summary>
    Task<IReadOnlyList<Guid>> GetAncestorChainEmployeeIdsAsync(
        Guid tenantId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Batched GetAncestorChainEmployeeIdsAsync: for every id in employeeIds, its full
    /// upward reporting chain ordered nearest-manager-first (Depth ascending), keyed by the
    /// descendant (subject) employee id. Ids with no ancestors are absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetAncestorChainsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default);
}
