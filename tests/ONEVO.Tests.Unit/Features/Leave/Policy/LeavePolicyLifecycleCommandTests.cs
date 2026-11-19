using FluentAssertions;
using ONEVO.Application.Features.Leave.Policy.Commands.AddLeavePolicyVersion;
using ONEVO.Application.Features.Leave.Policy.Commands.CloneLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.Commands.Common;
using ONEVO.Application.Features.Leave.Policy.Commands.DeleteLeavePolicyVersion;
using ONEVO.Application.Features.Leave.Policy.Commands.SetLeavePolicyActive;
using ONEVO.Application.Features.Leave.Policy.Commands.SetLeavePolicyAssignments;
using ONEVO.Application.Features.Leave.Policy.Commands.UpdateLeavePolicyDetails;
using ONEVO.Application.Features.Leave.Policy.Services;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class LeavePolicyLifecycleCommandTests
{
    private readonly PolicyTestHarness _h = new();

    private LeavePolicyAssignmentService Assignments() => new(_h.Repository, _h.UnitOfWork, _h.Clock.Object);

    private Task<ONEVO.Application.Common.Models.Result<Guid>> AddVersion(Guid policyId, DateOnly applyFrom, decimal hours = 10m) =>
        new AddLeavePolicyVersionCommandHandler(_h.Repository, _h.CurrentUser.Object, _h.Clock.Object, _h.UnitOfWork)
            .Handle(new AddLeavePolicyVersionCommand(policyId, applyFrom,
                PolicyTestHarness.Rules(hours, blackouts: [new LeavePolicyBlackoutInput(new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 26), "Holidays")])),
                CancellationToken.None);

    [Fact]
    public async Task AddVersion_rejects_past_apply_from()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId);

        var result = await AddVersion(policy.Id, PolicyTestHarness.Today.AddDays(-1));

        result.IsSuccess.Should().BeFalse();
        _h.Repository.Versions.Should().ContainSingle();
    }

    [Fact]
    public async Task AddVersion_increments_version_number_and_copies_blackouts()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId);

        var result = await AddVersion(policy.Id, new DateOnly(2026, 11, 1));

        result.IsSuccess.Should().BeTrue();
        var v2 = _h.Repository.Versions.Single(v => v.Id == result.Value);
        v2.VersionNumber.Should().Be(2);
        v2.EffectiveFrom.Should().Be(new DateOnly(2026, 11, 1));
        v2.AmountHours.Should().Be(10m);
        _h.Repository.Blackouts.Should().ContainSingle(b => b.LeavePolicyVersionId == v2.Id && b.Reason == "Holidays");
    }

    [Fact]
    public async Task AddVersion_applying_today_is_allowed()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId);

        var result = await AddVersion(policy.Id, PolicyTestHarness.Today);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AddVersion_replaces_future_version_with_same_date()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId);
        var first = await AddVersion(policy.Id, new DateOnly(2026, 11, 1), 10m);

        var second = await AddVersion(policy.Id, new DateOnly(2026, 11, 1), 12m);

        second.IsSuccess.Should().BeTrue();
        _h.Repository.Versions.Should().NotContain(v => v.Id == first.Value);
        _h.Repository.Versions.Single(v => v.Id == second.Value).Should()
            .Match<LeavePolicyVersion>(v => v.AmountHours == 12m && v.VersionNumber == 2);
        _h.Repository.Blackouts.Should().NotContain(b => b.LeavePolicyVersionId == first.Value);
    }

    [Fact]
    public async Task AddVersion_on_inactive_policy_is_rejected()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId);
        policy.IsActive = false;

        var result = await AddVersion(policy.Id, new DateOnly(2026, 11, 1));

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteVersion_rejects_effective_version()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId);
        await AddVersion(policy.Id, new DateOnly(2026, 11, 1));
        var v1 = _h.Repository.Versions.Single(v => v.VersionNumber == 1);

        var result = await new DeleteLeavePolicyVersionCommandHandler(_h.Repository, _h.CurrentUser.Object, _h.Clock.Object)
            .Handle(new DeleteLeavePolicyVersionCommand(policy.Id, v1.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _h.Repository.Versions.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteVersion_removes_future_version()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId);
        var future = await AddVersion(policy.Id, new DateOnly(2026, 11, 1));

        var result = await new DeleteLeavePolicyVersionCommandHandler(_h.Repository, _h.CurrentUser.Object, _h.Clock.Object)
            .Handle(new DeleteLeavePolicyVersionCommand(policy.Id, future.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _h.Repository.Versions.Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteVersion_rejects_only_version()
    {
        var policy = new LeavePolicy { Id = Guid.NewGuid(), TenantId = _h.TenantId, LeaveTypeId = _h.AnnualTypeId, Name = "Future" };
        _h.Repository.Policies.Add(policy);
        var only = new LeavePolicyVersion { Id = Guid.NewGuid(), TenantId = _h.TenantId, LeavePolicyId = policy.Id, VersionNumber = 1, EffectiveFrom = new DateOnly(2026, 12, 1) };
        _h.Repository.Versions.Add(only);

        var result = await new DeleteLeavePolicyVersionCommandHandler(_h.Repository, _h.CurrentUser.Object, _h.Clock.Object)
            .Handle(new DeleteLeavePolicyVersionCommand(policy.Id, only.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    private Task<ONEVO.Application.Common.Models.Result<LeavePolicyWriteResult>> SetAssignments(
        Guid policyId, IReadOnlyList<LeavePolicyAssignmentInput> targets, bool replace = false) =>
        new SetLeavePolicyAssignmentsCommandHandler(_h.Repository, _h.CurrentUser.Object, Assignments())
            .Handle(new SetLeavePolicyAssignmentsCommand(policyId, targets, replace), CancellationToken.None);

    [Fact]
    public async Task SetAssignments_conflict_without_replace_writes_nothing()
    {
        _h.SeedPolicy("Annual - sales", _h.AnnualTypeId, _h.Sales);
        var policy = _h.SeedPolicy("Annual - company", _h.AnnualTypeId, _h.Company);

        var result = await SetAssignments(policy.Id, [_h.Company, _h.Sales]);

        result.Value!.Conflicts.Should().ContainSingle(c => c.TargetId == _h.SalesId);
        _h.Repository.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task SetAssignments_replace_moves_target_to_this_policy()
    {
        var sales = _h.SeedPolicy("Annual - sales", _h.AnnualTypeId, _h.Sales);
        var policy = _h.SeedPolicy("Annual - company", _h.AnnualTypeId, _h.Company);

        var result = await SetAssignments(policy.Id, [_h.Company, _h.Sales], replace: true);

        result.Value!.PolicyId.Should().Be(policy.Id);
        _h.Repository.Assignments.Single(a => a.LeavePolicyId == sales.Id).EffectiveTo.Should().Be(PolicyTestHarness.Today.AddDays(-1));
        _h.Repository.Assignments.Where(a => a.LeavePolicyId == policy.Id && a.EffectiveTo == null)
            .Select(a => a.TargetId).Should().BeEquivalentTo([_h.CompanyId, _h.SalesId]);
    }

    [Fact]
    public async Task SetAssignments_removed_target_is_end_dated_not_deleted()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId, _h.Company, _h.Sales);

        await SetAssignments(policy.Id, [_h.Company]);

        _h.Repository.Assignments.Should().HaveCount(2);
        _h.Repository.Assignments.Single(a => a.TargetId == _h.SalesId).EffectiveTo.Should().Be(PolicyTestHarness.Today.AddDays(-1));
        _h.Repository.Assignments.Single(a => a.TargetId == _h.CompanyId).EffectiveTo.Should().BeNull();
    }

    [Fact]
    public async Task SetAssignments_keeps_employee_overrides()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId, _h.Company);
        _h.Repository.Assignments.Add(new LeavePolicyAssignment
        {
            Id = Guid.NewGuid(), TenantId = _h.TenantId, LeavePolicyId = policy.Id, LeaveTypeId = _h.AnnualTypeId,
            TargetKind = "employee", TargetId = _h.EmployeeId, EffectiveFrom = new DateOnly(2026, 5, 1),
        });

        await SetAssignments(policy.Id, []);

        _h.Repository.Assignments.Single(a => a.TargetId == _h.EmployeeId).EffectiveTo.Should().BeNull();
    }

    [Fact]
    public async Task SetAssignments_rejects_inactive_policy()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId);
        policy.IsActive = false;

        var result = await SetAssignments(policy.Id, [_h.Company]);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Deactivate_rejects_when_assigned()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId, _h.Company);

        var result = await new SetLeavePolicyActiveCommandHandler(_h.Repository, _h.CurrentUser.Object, _h.Clock.Object)
            .Handle(new SetLeavePolicyActiveCommand(policy.Id, false), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("leave_policy.has_assignments");
        policy.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Deactivate_and_reactivate_unassigned_policy()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId);
        var sut = new SetLeavePolicyActiveCommandHandler(_h.Repository, _h.CurrentUser.Object, _h.Clock.Object);

        (await sut.Handle(new SetLeavePolicyActiveCommand(policy.Id, false), CancellationToken.None)).IsSuccess.Should().BeTrue();
        policy.IsActive.Should().BeFalse();
        (await sut.Handle(new SetLeavePolicyActiveCommand(policy.Id, true), CancellationToken.None)).IsSuccess.Should().BeTrue();
        policy.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Clone_copies_version_in_force_without_assignments()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId, _h.Company);
        await AddVersion(policy.Id, new DateOnly(2026, 11, 1), 10m);

        var result = await new CloneLeavePolicyCommandHandler(_h.Repository, _h.CurrentUser.Object, _h.Clock.Object, _h.UnitOfWork)
            .Handle(new CloneLeavePolicyCommand(policy.Id, "Annual - copy"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var clone = _h.Repository.Policies.Single(p => p.Id == result.Value);
        clone.LeaveTypeId.Should().Be(_h.AnnualTypeId);
        var version = _h.Repository.Versions.Single(v => v.LeavePolicyId == clone.Id);
        version.VersionNumber.Should().Be(1);
        version.AmountHours.Should().Be(8m, "the version in force today is v1, not the future v2");
        version.EffectiveFrom.Should().Be(PolicyTestHarness.Today);
        _h.Repository.Assignments.Should().NotContain(a => a.LeavePolicyId == clone.Id);
    }

    [Fact]
    public async Task UpdateDetails_renames_and_rejects_duplicate_names()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId);
        _h.SeedPolicy("Taken", _h.AnnualTypeId);
        var sut = new UpdateLeavePolicyDetailsCommandHandler(_h.Repository, _h.CurrentUser.Object, _h.Clock.Object);

        (await sut.Handle(new UpdateLeavePolicyDetailsCommand(policy.Id, "Taken", null), CancellationToken.None))
            .StatusCode.Should().Be(409);
        (await sut.Handle(new UpdateLeavePolicyDetailsCommand(policy.Id, "Annual 2026", "New"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        policy.Name.Should().Be("Annual 2026");
        policy.Description.Should().Be("New");
    }
}
