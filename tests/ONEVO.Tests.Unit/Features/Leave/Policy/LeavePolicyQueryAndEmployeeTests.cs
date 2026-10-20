using FluentAssertions;
using Moq;
using ONEVO.Application.Features.Leave.Policy.Commands.EmployeeLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.Queries.GetEmployeeLeavePolicies;
using ONEVO.Application.Features.Leave.Policy.Queries.GetLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.Queries.ListLeavePolicies;
using ONEVO.Application.Features.Leave.Policy.Services;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class LeavePolicyQueryAndEmployeeTests
{
    private readonly PolicyTestHarness _h = new();
    private readonly Mock<ILeaveTypeRepository> _leaveTypes = new();
    private readonly Mock<ILeavePolicyResolver> _resolver = new();

    public LeavePolicyQueryAndEmployeeTests()
    {
        _leaveTypes.Setup(x => x.ListAsync(_h.TenantId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _h.Repository.LeaveTypes);
        foreach (var type in _h.Repository.LeaveTypes)
            type.Color = type.Id == _h.AnnualTypeId ? "#2563EB" : "#16A34A";
    }

    [Fact]
    public async Task List_shows_amount_and_assignment_summary()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId, _h.Company, _h.Sales, _h.Engineering, _h.Developer);
        _h.Repository.Versions.Add(new LeavePolicyVersion
        {
            Id = Guid.NewGuid(), TenantId = _h.TenantId, LeavePolicyId = policy.Id, VersionNumber = 2,
            EffectiveFrom = new DateOnly(2026, 11, 1), AmountHours = 10m, AmountPeriod = LeaveAmountPeriods.Month,
        });

        var result = await new ListLeavePoliciesQueryHandler(_h.Repository, _leaveTypes.Object, _h.CurrentUser.Object, _h.Clock.Object)
            .Handle(new ListLeavePoliciesQuery(false), CancellationToken.None);

        var item = result.Value!.Single();
        item.LeaveTypeName.Should().Be("Annual Leave");
        item.LeaveTypeColor.Should().Be("#2563EB");
        item.AmountHours.Should().Be(8m, "v1 is in force today");
        item.AmountPeriod.Should().Be(LeaveAmountPeriods.Month);
        item.CurrentVersionNumber.Should().Be(1);
        item.NextVersionEffectiveFrom.Should().Be(new DateOnly(2026, 11, 1));
        item.AssignmentCount.Should().Be(4);
        item.AssignedToSummary.Should().Equal("Whole company: Dapi Technologies", "2 departments", "Developer");
    }

    [Fact]
    public async Task Get_returns_versions_newest_first_with_is_future()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId, _h.Company);
        _h.Repository.Versions.Add(new LeavePolicyVersion
        {
            Id = Guid.NewGuid(), TenantId = _h.TenantId, LeavePolicyId = policy.Id, VersionNumber = 2,
            EffectiveFrom = new DateOnly(2026, 11, 1), AmountHours = 10m, AmountPeriod = LeaveAmountPeriods.Month,
        });

        var result = await new GetLeavePolicyQueryHandler(_h.Repository, _leaveTypes.Object, _h.CurrentUser.Object, _h.Clock.Object)
            .Handle(new GetLeavePolicyQuery(policy.Id), CancellationToken.None);

        var detail = result.Value!;
        detail.Versions.Select(v => v.VersionNumber).Should().Equal(2, 1);
        detail.Versions[0].IsFuture.Should().BeTrue();
        detail.Versions[1].IsCurrent.Should().BeTrue();
        detail.Current.VersionNumber.Should().Be(1);
        detail.Assignments.Should().ContainSingle(a => a.TargetName == "Dapi Technologies" && a.TargetKind == LeavePolicyTargetKinds.LegalEntity);
    }

    [Fact]
    public async Task EmployeePolicies_returns_row_per_active_type_with_source()
    {
        var annual = _h.SeedPolicy("Annual - company", _h.AnnualTypeId, _h.Company);
        var version = _h.Repository.Versions.Single(v => v.LeavePolicyId == annual.Id);
        _resolver.Setup(x => x.ResolveAllAsync(_h.TenantId, _h.EmployeeId, PolicyTestHarness.Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ResolvedLeavePolicy(_h.AnnualTypeId, annual.Id, annual.Name, version, [],
                LeavePolicyTargetKinds.LegalEntity, _h.CompanyId, "Dapi Technologies")]);

        var result = await new GetEmployeeLeavePoliciesQueryHandler(_resolver.Object, _leaveTypes.Object, _h.CurrentUser.Object, _h.Clock.Object)
            .Handle(new GetEmployeeLeavePoliciesQuery(_h.EmployeeId), CancellationToken.None);

        result.Value.Should().HaveCount(2, "inactive leave types are left out");
        var annualRow = result.Value!.Single(r => r.LeaveTypeId == _h.AnnualTypeId);
        annualRow.PolicyName.Should().Be("Annual - company");
        annualRow.SourceKind.Should().Be(LeavePolicyTargetKinds.LegalEntity);
        annualRow.SourceName.Should().Be("Dapi Technologies");
        annualRow.AmountHours.Should().Be(8m);
        annualRow.IsOverride.Should().BeFalse();
        var sickRow = result.Value!.Single(r => r.LeaveTypeId == _h.SickTypeId);
        sickRow.PolicyId.Should().BeNull();
    }

    private EmployeeLeavePolicyCommandHandler EmployeeSut() =>
        new(_h.Repository, _h.CurrentUser.Object, new LeavePolicyAssignmentService(_h.Repository, _h.UnitOfWork, _h.Clock.Object), _h.UnitOfWork);

    [Fact]
    public async Task SetEmployeePolicy_rejects_policy_for_other_leave_type()
    {
        var sick = _h.SeedPolicy("Sick", _h.SickTypeId);

        var result = await EmployeeSut().Handle(new SetEmployeeLeavePolicyCommand(_h.EmployeeId, _h.AnnualTypeId, sick.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _h.Repository.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task SetEmployeePolicy_upserts_override()
    {
        var first = _h.SeedPolicy("Annual - A", _h.AnnualTypeId);
        var second = _h.SeedPolicy("Annual - B", _h.AnnualTypeId);
        _h.Repository.Assignments.Add(new LeavePolicyAssignment
        {
            Id = Guid.NewGuid(), TenantId = _h.TenantId, LeavePolicyId = first.Id, LeaveTypeId = _h.AnnualTypeId,
            TargetKind = LeavePolicyTargetKinds.Employee, TargetId = _h.EmployeeId, EffectiveFrom = new DateOnly(2026, 3, 1),
        });

        var result = await EmployeeSut().Handle(new SetEmployeeLeavePolicyCommand(_h.EmployeeId, _h.AnnualTypeId, second.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _h.Repository.Assignments.Single(a => a.LeavePolicyId == first.Id).EffectiveTo.Should().Be(PolicyTestHarness.Today.AddDays(-1));
        _h.Repository.Assignments.Should().ContainSingle(a => a.LeavePolicyId == second.Id && a.EffectiveTo == null
            && a.TargetKind == LeavePolicyTargetKinds.Employee && a.TargetId == _h.EmployeeId);
    }

    [Fact]
    public async Task SetEmployeePolicy_twice_same_day_updates_todays_row()
    {
        var first = _h.SeedPolicy("Annual - A", _h.AnnualTypeId);
        var second = _h.SeedPolicy("Annual - B", _h.AnnualTypeId);

        await EmployeeSut().Handle(new SetEmployeeLeavePolicyCommand(_h.EmployeeId, _h.AnnualTypeId, first.Id), CancellationToken.None);
        await EmployeeSut().Handle(new SetEmployeeLeavePolicyCommand(_h.EmployeeId, _h.AnnualTypeId, second.Id), CancellationToken.None);

        _h.Repository.Assignments.Should().ContainSingle(a => a.TargetId == _h.EmployeeId)
            .Which.LeavePolicyId.Should().Be(second.Id);
    }

    [Fact]
    public async Task SetEmployeePolicy_rejects_inactive_policy()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId);
        policy.IsActive = false;

        var result = await EmployeeSut().Handle(new SetEmployeeLeavePolicyCommand(_h.EmployeeId, _h.AnnualTypeId, policy.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Leave_create_holder_cannot_change_their_own_policy()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId);
        _h.Repository.OwnEmployeeIds.Add(_h.EmployeeId);
        _h.CurrentUser.Setup(x => x.HasPermission("leave:manage")).Returns(false);

        var set = await EmployeeSut().Handle(new SetEmployeeLeavePolicyCommand(_h.EmployeeId, _h.AnnualTypeId, policy.Id), CancellationToken.None);
        var reset = await EmployeeSut().Handle(new ResetEmployeeLeavePolicyCommand(_h.EmployeeId, _h.AnnualTypeId), CancellationToken.None);

        set.StatusCode.Should().Be(403);
        reset.StatusCode.Should().Be(403);
        _h.Repository.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Leave_manage_holder_can_change_their_own_policy()
    {
        var policy = _h.SeedPolicy("Annual", _h.AnnualTypeId);
        _h.Repository.OwnEmployeeIds.Add(_h.EmployeeId);
        _h.CurrentUser.Setup(x => x.HasPermission("leave:manage")).Returns(true);

        var set = await EmployeeSut().Handle(new SetEmployeeLeavePolicyCommand(_h.EmployeeId, _h.AnnualTypeId, policy.Id), CancellationToken.None);

        set.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ResetEmployeePolicy_end_dates_override_only()
    {
        var company = _h.SeedPolicy("Annual - company", _h.AnnualTypeId, _h.Company);
        var mine = _h.SeedPolicy("Annual - mine", _h.AnnualTypeId);
        _h.Repository.Assignments.Add(new LeavePolicyAssignment
        {
            Id = Guid.NewGuid(), TenantId = _h.TenantId, LeavePolicyId = mine.Id, LeaveTypeId = _h.AnnualTypeId,
            TargetKind = LeavePolicyTargetKinds.Employee, TargetId = _h.EmployeeId, EffectiveFrom = new DateOnly(2026, 3, 1),
        });

        var result = await EmployeeSut().Handle(new ResetEmployeeLeavePolicyCommand(_h.EmployeeId, _h.AnnualTypeId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _h.Repository.Assignments.Single(a => a.LeavePolicyId == mine.Id).EffectiveTo.Should().Be(PolicyTestHarness.Today.AddDays(-1));
        _h.Repository.Assignments.Single(a => a.LeavePolicyId == company.Id).EffectiveTo.Should().BeNull();
    }
}
