using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Application.Features.TimeAttendance.Services;

/// <summary>Filters a legal entity's policies down to the active, full-company-scoped ones that
/// cover a given work date. Scoping beyond full_company (department/position/employee) is not
/// resolved anywhere today despite ClockInPolicy having the fields for it - out of scope here,
/// matches AttendanceTodayStateService's existing behavior exactly. Shared by
/// AttendanceTodayStateService and EfEmployeeRepository so both read "which policy applies" the
/// same way instead of duplicating the filter.</summary>
public static class ClockInPolicyResolver
{
    public static IReadOnlyList<ClockInPolicy> ResolveActiveFullCompanyPolicies(
        IEnumerable<ClockInPolicy> policies, DateOnly workDate)
        => policies
            .Where(policy => policy.IsActive
                && policy.ScopeType == ClockInPolicy.ScopeFullCompany
                && policy.EffectiveFrom <= workDate
                && (policy.EffectiveTo is null || policy.EffectiveTo >= workDate))
            .ToList();
}
