using FluentAssertions;
using FluentValidation.TestHelper;
using ONEVO.Application.Features.Leave.Policy.Commands.Common;
using ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.Services;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class CreateLeavePolicyCommandHandlerTests
{
    private readonly PolicyTestHarness _h = new();

    private CreateLeavePolicyCommandHandler Sut() => new(
        _h.Repository, _h.CurrentUser.Object, _h.Clock.Object,
        new LeavePolicyAssignmentService(_h.Repository, _h.UnitOfWork, _h.Clock.Object));

    private CreateLeavePolicyCommand Command(
        IReadOnlyList<LeavePolicyAssignmentInput>? assignments = null, bool replace = false, Guid? leaveTypeId = null,
        string name = "Annual - standard") =>
        new(leaveTypeId ?? _h.AnnualTypeId, name, "desc",
            PolicyTestHarness.Rules(blackouts: [new LeavePolicyBlackoutInput(new DateOnly(2026, 12, 20), new DateOnly(2026, 12, 31), "Year end")]),
            assignments ?? [_h.Company], replace);

    [Fact]
    public async Task Create_writes_policy_v1_and_assignments()
    {
        var result = await Sut().Handle(Command([_h.Company, _h.Sales]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var policyId = result.Value!.PolicyId!.Value;
        var policy = _h.Repository.Policies.Single(p => p.Id == policyId);
        policy.LeaveTypeId.Should().Be(_h.AnnualTypeId);
        var version = _h.Repository.Versions.Single(v => v.LeavePolicyId == policyId);
        version.VersionNumber.Should().Be(1);
        version.EffectiveFrom.Should().Be(PolicyTestHarness.Today);
        version.AmountHours.Should().Be(8m);
        version.AmountPeriod.Should().Be(LeaveAmountPeriods.Month);
        version.CreatedBy.Should().Be(_h.UserId);
        _h.Repository.Blackouts.Should().ContainSingle(b => b.LeavePolicyVersionId == version.Id && b.Reason == "Year end");
        _h.Repository.Assignments.Where(a => a.LeavePolicyId == policyId).Should().HaveCount(2)
            .And.OnlyContain(a => a.EffectiveFrom == PolicyTestHarness.Today && a.EffectiveTo == null
                && a.LeaveTypeId == _h.AnnualTypeId);
    }

    [Fact]
    public async Task Create_rejects_inactive_leave_type()
    {
        var result = await Sut().Handle(Command(leaveTypeId: _h.InactiveTypeId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _h.Repository.Policies.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_returns_conflict_when_target_already_has_policy_for_type()
    {
        var existing = _h.SeedPolicy("Annual - sales", _h.AnnualTypeId, _h.Sales);

        var result = await Sut().Handle(Command([_h.Company, _h.Sales]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PolicyId.Should().BeNull();
        result.Value.Conflicts.Should().ContainSingle().Which.Should().Be(new LeavePolicyAssignmentConflict(
            LeavePolicyTargetKinds.Department, _h.SalesId, "Sales", existing.Id, "Annual - sales"));
        _h.Repository.Policies.Should().ContainSingle();
        _h.Repository.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Same_target_with_a_policy_for_another_leave_type_is_not_a_conflict()
    {
        _h.SeedPolicy("Sick - sales", _h.SickTypeId, _h.Sales);

        var result = await Sut().Handle(Command([_h.Sales]), CancellationToken.None);

        result.Value!.PolicyId.Should().NotBeNull();
        result.Value.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_with_replace_ends_existing_assignment_yesterday_and_adds_new_from_today()
    {
        var existing = _h.SeedPolicy("Annual - sales", _h.AnnualTypeId, _h.Sales);

        var result = await Sut().Handle(Command([_h.Sales], replace: true), CancellationToken.None);

        result.Value!.PolicyId.Should().NotBeNull();
        var old = _h.Repository.Assignments.Single(a => a.LeavePolicyId == existing.Id);
        old.EffectiveTo.Should().Be(PolicyTestHarness.Today.AddDays(-1));
        old.EndedBy.Should().Be(_h.UserId);
        _h.Repository.Assignments.Should().ContainSingle(a =>
            a.LeavePolicyId == result.Value.PolicyId && a.TargetId == _h.SalesId && a.EffectiveTo == null);
    }

    [Fact]
    public async Task Create_rejects_unknown_target()
    {
        var unknown = new LeavePolicyAssignmentInput(LeavePolicyTargetKinds.Department, Guid.NewGuid());

        var result = await Sut().Handle(Command([unknown]), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _h.Repository.Policies.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_rejects_duplicate_name()
    {
        _h.SeedPolicy("Annual - standard", _h.AnnualTypeId);

        var result = await Sut().Handle(Command(), CancellationToken.None);

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public void Validator_requires_name_and_rejects_employee_level_and_duplicate_targets()
    {
        var validator = new CreateLeavePolicyCommandValidator();

        validator.TestValidate(Command(name: "")).ShouldHaveValidationErrorFor(x => x.Name);
        validator.TestValidate(Command([new LeavePolicyAssignmentInput(LeavePolicyTargetKinds.Employee, _h.EmployeeId)]))
            .ShouldHaveValidationErrorFor(x => x.Assignments);
        validator.TestValidate(Command([_h.Sales, _h.Sales])).ShouldHaveValidationErrorFor(x => x.Assignments);
        validator.TestValidate(Command()).ShouldNotHaveAnyValidationErrors();
    }
}
