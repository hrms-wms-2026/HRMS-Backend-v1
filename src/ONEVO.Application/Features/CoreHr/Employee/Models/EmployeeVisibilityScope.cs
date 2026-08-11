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
    public static EmployeeVisibilityScope Unrestricted() =>
        new(true, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
}
