using ONEVO.Application.Features.CoreHr.Employee.Models;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class EmployeeVisibilityScopeMatcherTests
{
    private static readonly Guid Emp = Guid.NewGuid(), Pos = Guid.NewGuid(), Dept = Guid.NewGuid(), Le = Guid.NewGuid();

    private static EmployeeVisibilityScope Scope(Guid? own = null, Guid? pos = null, Guid? dept = null, Guid? le = null) => new(
        false, own,
        pos is null ? new HashSet<Guid>() : new HashSet<Guid> { pos.Value },
        dept is null ? new HashSet<Guid>() : new HashSet<Guid> { dept.Value },
        le is null ? new HashSet<Guid>() : new HashSet<Guid> { le.Value });

    [Fact] public void Unrestricted_includes_everyone() => Assert.True(EmployeeVisibilityScopeMatcher.Includes(EmployeeVisibilityScope.Unrestricted(), Emp, null, null, null));
    [Fact] public void Own_employee_is_included() => Assert.True(EmployeeVisibilityScopeMatcher.Includes(Scope(own: Emp), Emp, null, null, null));
    [Fact] public void Covered_primary_position_is_included() => Assert.True(EmployeeVisibilityScopeMatcher.Includes(Scope(pos: Pos), Emp, Pos, null, null));
    [Fact] public void Covered_department_is_included() => Assert.True(EmployeeVisibilityScopeMatcher.Includes(Scope(dept: Dept), Emp, null, Dept, null));
    [Fact] public void Company_wide_legal_entity_is_included() => Assert.True(EmployeeVisibilityScopeMatcher.Includes(Scope(le: Le), Emp, null, null, Le));
    [Fact] public void Sub_department_is_NOT_included_raw_coverage_only() => Assert.False(EmployeeVisibilityScopeMatcher.Includes(Scope(dept: Dept), Emp, null, Guid.NewGuid(), null));
    [Fact] public void Nothing_matching_is_excluded() => Assert.False(EmployeeVisibilityScopeMatcher.Includes(Scope(), Emp, Pos, Dept, Le));

    // Review clarification (2026-09-30): extended coverage for boundary/edge subjects.
    [Fact] public void Employee_with_no_department_never_matches_department_coverage() =>
        Assert.False(EmployeeVisibilityScopeMatcher.Includes(Scope(dept: Dept), Emp, null, null, null));

    [Fact] public void Employee_with_no_primary_position_never_matches_position_coverage() =>
        Assert.False(EmployeeVisibilityScopeMatcher.Includes(Scope(pos: Pos), Emp, null, null, null));

    [Fact] public void Employee_in_another_legal_entity_never_matches_this_legal_entitys_company_wide_coverage() =>
        Assert.False(EmployeeVisibilityScopeMatcher.Includes(Scope(le: Le), Emp, null, null, Guid.NewGuid()));

    [Fact] public void Own_employee_is_included_even_when_also_covered_by_department()
    {
        var scope = Scope(own: Emp, dept: Dept);
        Assert.True(EmployeeVisibilityScopeMatcher.Includes(scope, Emp, null, Dept, null));
        Assert.True(EmployeeVisibilityScopeMatcher.Includes(scope, Emp, null, null, null)); // own alone is enough
    }

    [Fact] public void No_scope_dimension_set_never_matches_anyone()
    {
        var scope = Scope();
        Assert.False(EmployeeVisibilityScopeMatcher.Includes(scope, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
    }
}
