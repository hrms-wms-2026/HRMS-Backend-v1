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
    public void Hybrid_SourceRule_Invalid_Fails()
    {
        var rules = ValidWorkAreaRules() with
        {
            Hybrid = ValidWorkAreaRules().Hybrid with { SourceRule = "invalid" }
        };
        var result = _validator.TestValidate(ValidCommand() with { WorkAreaRules = rules });
        result.ShouldHaveValidationErrorFor("WorkAreaRules.Hybrid.SourceRule");
    }

    [Fact]
    public void Field_PhotoRequirement_Invalid_Fails()
    {
        var rules = ValidWorkAreaRules() with
        {
            Field = ValidWorkAreaRules().Field with { PhotoRequirement = "always" }
        };
        var result = _validator.TestValidate(ValidCommand() with { WorkAreaRules = rules });
        result.ShouldHaveValidationErrorFor("WorkAreaRules.Field.PhotoRequirement");
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

    [Fact]
    public void AllowedRadiusMeters_Required_When_LocationVerification_Enabled()
    {
        var cmd = ValidCommand() with
        {
            LocationVerificationRequired = true,
            AllowedRadiusMeters = null
        };
        var result = _validator.TestValidate(cmd);
        Assert.False(result.IsValid);
    }

    private static CreateClockInPolicyCommand ValidCommand()
        => new(
            Guid.NewGuid(),
            "Default Clock-in Policy",
            new ClockInPolicyScopeInput(ClockInPolicyEntity.ScopeFullCompany, null, null, null),
            new DateOnly(2026, 8, 21),
            null,
            true,
            100,
            ValidWorkAreaRules(),
            true,
            ClockInPolicyEntity.NotificationManagementCoverageOwner,
            [new LateDeductionRuleInput(15, 0, Guid.NewGuid())],
            true);

    private static WorkAreaRulesInput ValidWorkAreaRules()
        => new(
            new WorkAreaSourceRulesInput(true, false, false, false),
            new WorkAreaSourceRulesInput(false, true, true, true),
            new HybridWorkAreaRulesInput(false, true, true, true, true, ClockInPolicyEntity.HybridSourceEmployeeChoice),
            new FieldWorkAreaRulesInput(false, true, true, ClockInPolicyEntity.FieldPhotoRequired));
}
