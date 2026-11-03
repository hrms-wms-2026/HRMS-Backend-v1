namespace ONEVO.Application.Features.CoreHr.Employee.Models;

/// <summary>
/// Resolved visibility scope for one caller, derived from management_coverage_records
/// via the caller's own active PrimaryEmployment position. Employees with no active
/// primary assignment (including every employee seeded before this feature existed)
/// cannot be scoped by position/department coverage and are visible only through
/// CanViewAllTenantEmployees or CompanyWideLegalEntityIds - a documented Phase 1
/// limitation, not a bug.
/// </summary>
public sealed record EmployeeVisibilityScope(
    bool CanViewAllTenantEmployees,
    Guid? OwnEmployeeId,
    IReadOnlySet<Guid> CoveredPositionIds,
    IReadOnlySet<Guid> CoveredDepartmentIds,
    IReadOnlySet<Guid> CompanyWideLegalEntityIds)
{
    private static readonly IReadOnlySet<Guid> NoEmployees = new HashSet<Guid>();

    /// <summary>
    /// Employees reached by expanding the coverage rows the same way
    /// IEmployeeAuthorityResolver.ResolveVisibilityAsync does for the Employees list: holders of
    /// each covered position plus their whole reporting-line subtree (employee hierarchy
    /// closure), and employees in descendant departments of each covered department - each
    /// restricted to the coverage row's legal entity. The positional sets above only match
    /// DIRECT coverage, so without this a skip-level manager saw an employee in the list but got
    /// 403 opening them. Unlike the list it is not narrowed to "active" status, so offboarding
    /// and on-leave reports stay reachable exactly as direct coverage already allowed.
    /// </summary>
    public IReadOnlySet<Guid> CoveredEmployeeIds { get; init; } = NoEmployees;

    public static EmployeeVisibilityScope Unrestricted() =>
        new(true, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
}
