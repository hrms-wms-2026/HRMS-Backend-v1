using FluentValidation.TestHelper;
using ONEVO.Application.Features.TimeAttendance.Commands.CreateClockInPolicy;
using ONEVO.Application.Features.TimeAttendance.Models;
using ClockInPolicyEntity = ONEVO.Domain.Features.TimeAttendance.Entities.ClockInPolicy;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance.ClockInPolicies;

public class CreateClockInPolicyCommandValidatorTests
{
    private readonly CreateClockInPolicyCommandValidator _validator = new();

    [Fact]
    public void Valid_FullCompany_Policy_Passes()
    {
        var result = _validator.TestValidate(ValidCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Department_Scope_Requires_DepartmentIds()
    {
        var cmd = ValidCommand() with
        {
            Scope = new ClockInPolicyScopeInput(ClockInPolicyEntity.ScopeDepartment, null, null, null)
        };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Scope);
    }

    [Fact]
    public void Position_Scope_Requires_PositionIds()
    {
        var cmd = ValidCommand() with
        {
            Scope = new ClockInPolicyScopeInput(ClockInPolicyEntity.ScopePosition, null, null, null)
        };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Scope);
    }

    [Fact]
    public void Employee_Scope_Requires_EmployeeIds()
    {
        var cmd = ValidCommand() with
        {
            Scope = new ClockInPolicyScopeInput(ClockInPolicyEntity.ScopeEmployee, null, null, null)
        };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Scope);
    }

    [Fact]
    public void EffectiveTo_Before_EffectiveFrom_Fails()
    {
        var cmd = ValidCommand() with
        {
            EffectiveFrom = new DateOnly(2026, 8, 21),
            EffectiveTo = new DateOnly(2026, 8, 1)
        };
        var result = _validator.TestValidate(cmd);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Duplicate_LateDeduction_Bracket_Fails()
    {
        var cmd = ValidCommand() with
        {
            LateDeductionRules =
            [
                new LateDeductionRuleInput(15, 0, Guid.NewGuid()),
                new LateDeductionRuleInput(15, 1, Guid.NewGuid())
            ]
        };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.LateDeductionRules);
    }

    private static CreateClockInPolicyCommand ValidCommand()
        => new(
            Guid.NewGuid(),
            "Default Clock-in Policy",
            new ClockInPolicyScopeInput(ClockInPolicyEntity.ScopeFullCompany, null, null, null),
            new DateOnly(2026, 8, 21),
            null,
            true,
            ClockInPolicyEntity.NotificationManagementCoverageOwner,
            [new LateDeductionRuleInput(15, 0, Guid.NewGuid())],
            true);
}
