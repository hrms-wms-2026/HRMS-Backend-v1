using FluentValidation.TestHelper;
using ONEVO.Application.Features.Leave.Type.Commands.CreateLeaveType;
using ONEVO.Application.Features.Leave.Type.Commands.UpdateLeaveType;
using ONEVO.Application.Features.OrgStructure.Commands.CreateDepartment;
using ONEVO.Application.Features.OrgStructure.Commands.CreatePosition;
using ONEVO.Application.Features.OrgStructure.Commands.UpdateDepartment;
using ONEVO.Application.Features.OrgStructure.Commands.UpdatePosition;
using ONEVO.Application.Features.TimeAttendance.Commands.CreateClockInPolicy;
using ONEVO.Application.Features.TimeAttendance.Commands.CreateWorkMode;
using ONEVO.Application.Features.TimeAttendance.Commands.UpdateClockInPolicy;
using ONEVO.Application.Features.TimeAttendance.Commands.UpdateWorkMode;
using ONEVO.Application.Features.TimeAttendance.Models;

namespace ONEVO.Tests.Unit.Features.Validation;

/// <summary>Task 19 moved the rules each Create/Update pair shared into one base validator. These tests pin that both
/// sides still apply the shared rules, with the same messages, plus the rules only one side had.</summary>
public sealed class SharedCreateUpdateValidatorTests
{
    private static readonly ClockInPolicyScopeInput FullCompany = new("full_company", null, null, null);
    private static readonly DateOnly From = new(2026, 1, 1);

    // ---- Clock-in policy ----

    private static CreateClockInPolicyCommand CreatePolicy(string name = "Default", DateOnly? to = null,
        IReadOnlyList<LateDeductionRuleInput>? rules = null, string resolver = "manager", ClockInPolicyScopeInput? scope = null)
        => new(Guid.NewGuid(), name, scope ?? FullCompany, From, to, false, resolver, rules ?? [], true);

    private static UpdateClockInPolicyCommand UpdatePolicy(string name = "Default", DateOnly? to = null,
        IReadOnlyList<LateDeductionRuleInput>? rules = null, string resolver = "manager", Guid? policyId = null)
        => new(Guid.NewGuid(), policyId ?? Guid.NewGuid(), name, FullCompany, From, to, false, resolver, rules ?? [], true);

    [Fact]
    public void ClockInPolicy_ValidInput_PassesOnBothSides()
    {
        new CreateClockInPolicyCommandValidator().TestValidate(CreatePolicy()).ShouldNotHaveAnyValidationErrors();
        new UpdateClockInPolicyCommandValidator().TestValidate(UpdatePolicy()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ClockInPolicy_SharedRules_ApplyOnBothSides()
    {
        var duplicateMinutes = new List<LateDeductionRuleInput> { new(10, 1m, Guid.NewGuid()), new(10, 2m, Guid.NewGuid()) };

        var create = new CreateClockInPolicyCommandValidator().TestValidate(
            CreatePolicy(name: new string('n', 121), to: From.AddDays(-1), rules: duplicateMinutes, resolver: ""));
        var update = new UpdateClockInPolicyCommandValidator().TestValidate(
            UpdatePolicy(name: new string('n', 121), to: From.AddDays(-1), rules: duplicateMinutes, resolver: ""));

        foreach (var result in new[] { create.Errors, update.Errors })
        {
            Assert.Contains(result, e => e.ErrorMessage == "Policy name cannot exceed 120 characters.");
            Assert.Contains(result, e => e.ErrorMessage == "Effective to must be greater than or equal to effective from.");
            Assert.Contains(result, e => e.ErrorMessage == "Duplicate late arrival minute values are not allowed in the same policy.");
            Assert.Contains(result, e => e.ErrorMessage == "Notification recipient resolver is required.");
        }
    }

    [Fact]
    public void ClockInPolicy_ScopeAndLateRuleItems_StillChecked()
    {
        new CreateClockInPolicyCommandValidator()
            .TestValidate(CreatePolicy(scope: new ClockInPolicyScopeInput("galaxy", null, null, null)))
            .ShouldHaveValidationErrorFor(x => x.Scope);
        new UpdateClockInPolicyCommandValidator()
            .TestValidate(UpdatePolicy(rules: [new LateDeductionRuleInput(0, -1m, Guid.Empty)]))
            .ShouldHaveValidationErrorFor("LateDeductionRules[0].LateArrivalMinute");
    }

    [Fact]
    public void ClockInPolicy_UpdateOnly_RequiresPolicyId()
        => new UpdateClockInPolicyCommandValidator().TestValidate(UpdatePolicy(policyId: Guid.Empty))
            .ShouldHaveValidationErrorFor(x => x.PolicyId).WithErrorMessage("Policy ID is required.");

    // ---- Position ----

    [Fact]
    public void Position_SharedRules_ApplyOnBothSides()
    {
        var create = new CreatePositionCommandValidator().TestValidate(
            new CreatePositionCommand(Guid.Empty, Guid.Empty, "", "BAD CODE", 0, null));
        var update = new UpdatePositionCommandValidator().TestValidate(
            new UpdatePositionCommand(Guid.Empty, Guid.NewGuid(), Guid.Empty, "", "BAD CODE", 0, null));

        foreach (var result in new[] { create.Errors, update.Errors })
        {
            Assert.Contains(result, e => e.ErrorMessage == "Legal entity ID is required.");
            Assert.Contains(result, e => e.ErrorMessage == "Department ID is required.");
            Assert.Contains(result, e => e.ErrorMessage == "Position name is required.");
            Assert.Contains(result, e => e.ErrorMessage == "Position code cannot exceed 5 characters.");
            Assert.Contains(result, e => e.ErrorMessage == "Position code may only contain letters, numbers, hyphens, and underscores.");
            Assert.Contains(result, e => e.ErrorMessage == "Capacity must be at least 1.");
        }
    }

    [Fact]
    public void Position_Valid_Passes_AndUpdateOnlyRulesStay()
    {
        new CreatePositionCommandValidator().TestValidate(new CreatePositionCommand(Guid.NewGuid(), Guid.NewGuid(), "Lead", "LD-1", 1, null))
            .ShouldNotHaveAnyValidationErrors();
        var id = Guid.NewGuid();
        var result = new UpdatePositionCommandValidator().TestValidate(
            new UpdatePositionCommand(Guid.NewGuid(), id, Guid.NewGuid(), "Lead", "LD", 2, id));
        Assert.Contains(result.Errors, e => e.ErrorMessage == "A position cannot report to itself.");
    }

    // ---- Department ----

    [Fact]
    public void Department_SharedRules_ApplyOnBothSides_AndUpdateOnlyRulesStay()
    {
        new CreateDepartmentCommandValidator().TestValidate(new CreateDepartmentCommand(Guid.NewGuid(), "Finance", "FIN", null, null))
            .ShouldNotHaveAnyValidationErrors();

        var create = new CreateDepartmentCommandValidator().TestValidate(
            new CreateDepartmentCommand(Guid.Empty, new string('n', 101), "has space", null, null));
        var id = Guid.NewGuid();
        var update = new UpdateDepartmentCommandValidator().TestValidate(
            new UpdateDepartmentCommand(Guid.Empty, id, new string('n', 101), "has space", id, null));

        foreach (var result in new[] { create.Errors, update.Errors })
        {
            Assert.Contains(result, e => e.ErrorMessage == "Legal entity ID is required.");
            Assert.Contains(result, e => e.ErrorMessage == "Department name cannot exceed 100 characters.");
            Assert.Contains(result, e => e.ErrorMessage == "Department code may only contain letters, numbers, hyphens, and underscores.");
        }
        Assert.Contains(update.Errors, e => e.ErrorMessage == "Department cannot be its own parent.");
        Assert.DoesNotContain(create.Errors, e => e.ErrorMessage == "Department cannot be its own parent.");
    }

    // ---- Leave type ----

    [Fact]
    public void LeaveType_SharedRules_ApplyOnBothSides_CodeOnlyOnCreate()
    {
        var create = new CreateLeaveTypeCommandValidator().TestValidate(
            new CreateLeaveTypeCommand(new string('n', 101), "", null, "red", true, "everyone", true, 0m, []));
        var update = new UpdateLeaveTypeCommandValidator().TestValidate(
            new UpdateLeaveTypeCommand(Guid.NewGuid(), new string('n', 101), null, "red", true, "everyone", true, 0m, []));

        foreach (var result in new[] { create.Errors, update.Errors })
        {
            Assert.Contains(result, e => e.ErrorMessage == "Leave type name is required and cannot exceed 100 characters.");
            Assert.Contains(result, e => e.ErrorMessage == "Colour must be a #RRGGBB hex colour.");
            Assert.Contains(result, e => e.ErrorMessage == "Applicable gender must be one of: all, male, female.");
            Assert.Contains(result, e => e.ErrorMessage == "Document-required threshold must be more than 0 hours.");
        }
        Assert.Contains(create.Errors, e => e.PropertyName == "Code");
        Assert.DoesNotContain(update.Errors, e => e.PropertyName == "Code");
    }

    [Fact]
    public void LeaveType_Valid_PassesOnBothSides()
    {
        new CreateLeaveTypeCommandValidator().TestValidate(
                new CreateLeaveTypeCommand("Annual", "AL", null, "#22AA66", true, "all", false, null, []))
            .ShouldNotHaveAnyValidationErrors();
        new UpdateLeaveTypeCommandValidator().TestValidate(
                new UpdateLeaveTypeCommand(Guid.NewGuid(), "Annual", null, "#22AA66", true, "all", false, null, []))
            .ShouldNotHaveAnyValidationErrors();
    }

    // ---- Work mode ----

    [Fact]
    public void WorkMode_SharedRules_ApplyOnBothSides_IdRulesStay()
    {
        var create = new CreateWorkModeCommandValidator().TestValidate(
            new CreateWorkModeCommand(Guid.Empty, " ", true, true, true, false, true, true));
        var update = new UpdateWorkModeCommandValidator().TestValidate(
            new UpdateWorkModeCommand(Guid.Empty, new string('n', 121), true, true, true, false, true, true));

        Assert.Contains(create.Errors, e => e.ErrorMessage == "Work mode name is required.");
        Assert.Contains(update.Errors, e => e.ErrorMessage == "Work mode name cannot exceed 120 characters.");
        foreach (var result in new[] { create.Errors, update.Errors })
            Assert.Contains(result, e => e.ErrorMessage.StartsWith("A work mode cannot both self-register", StringComparison.Ordinal));
        Assert.Contains(create.Errors, e => e.ErrorMessage == "Legal entity ID is required.");
        Assert.Contains(update.Errors, e => e.PropertyName == "Id");

        new CreateWorkModeCommandValidator().TestValidate(new CreateWorkModeCommand(Guid.NewGuid(), "Office", true, true, true, false, false, false))
            .ShouldNotHaveAnyValidationErrors();
    }
}
