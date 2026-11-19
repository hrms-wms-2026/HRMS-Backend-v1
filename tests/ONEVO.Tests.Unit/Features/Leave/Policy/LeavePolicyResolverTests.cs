using FluentAssertions;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.Services;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Policy.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class LeavePolicyResolverTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 20);

    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _departmentId = Guid.NewGuid();
    private readonly Guid _parentDepartmentId = Guid.NewGuid();
    private readonly Guid _positionId = Guid.NewGuid();
    private readonly Guid _annual = Guid.NewGuid();
    private readonly Guid _sick = Guid.NewGuid();
    private readonly Guid _casual = Guid.NewGuid();

    private readonly List<LeaveResolutionPosition> _positions = [];
    private readonly List<LeaveResolutionDepartment> _departments = [];
    private readonly List<LeaveResolutionPolicy> _policies = [];
    private readonly List<LeavePolicyVersion> _versions = [];
    private readonly List<LeavePolicyAssignment> _assignments = [];
    private Guid? _employeeDepartmentId;

    public LeavePolicyResolverTests()
    {
        _employeeDepartmentId = _departmentId;
        _departments.Add(new LeaveResolutionDepartment(_departmentId, _parentDepartmentId));
        _departments.Add(new LeaveResolutionDepartment(_parentDepartmentId, null));
        _positions.Add(new LeaveResolutionPosition(_employeeId, _positionId, new DateOnly(2024, 1, 1), null));
    }

    [Fact]
    public async Task Company_has_annual_and_sick_position_has_casual_and_sick()
    {
        var companyAnnual = Policy("Company annual", _annual);
        var companySick = Policy("Company sick", _sick);
        var positionCasual = Policy("Position casual", _casual);
        var positionSick = Policy("Position sick", _sick);
        Assign(companyAnnual, LeavePolicyTargetKinds.LegalEntity, _legalEntityId);
        Assign(companySick, LeavePolicyTargetKinds.LegalEntity, _legalEntityId);
        Assign(positionCasual, LeavePolicyTargetKinds.Position, _positionId);
        Assign(positionSick, LeavePolicyTargetKinds.Position, _positionId);

        var resolved = await Sut().ResolveAllAsync(TenantId, _employeeId, Today, CancellationToken.None);

        resolved.Should().HaveCount(3);
        resolved.Single(r => r.LeaveTypeId == _annual).Should().Match<ResolvedLeavePolicy>(r =>
            r.PolicyId == companyAnnual && r.SourceKind == LeavePolicyTargetKinds.LegalEntity);
        resolved.Single(r => r.LeaveTypeId == _casual).Should().Match<ResolvedLeavePolicy>(r =>
            r.PolicyId == positionCasual && r.SourceKind == LeavePolicyTargetKinds.Position);
        resolved.Single(r => r.LeaveTypeId == _sick).Should().Match<ResolvedLeavePolicy>(r =>
            r.PolicyId == positionSick && r.SourceKind == LeavePolicyTargetKinds.Position);
    }

    [Fact]
    public async Task Employee_override_beats_position()
    {
        var positionSick = Policy("Position sick", _sick);
        var mySick = Policy("My sick", _sick);
        Assign(positionSick, LeavePolicyTargetKinds.Position, _positionId);
        Assign(mySick, LeavePolicyTargetKinds.Employee, _employeeId);

        var resolved = await Sut().ResolveAsync(TenantId, _employeeId, _sick, Today, CancellationToken.None);

        resolved!.PolicyId.Should().Be(mySick);
        resolved.SourceKind.Should().Be(LeavePolicyTargetKinds.Employee);
    }

    [Fact]
    public async Task Department_parent_assignment_applies_to_child_department()
    {
        var parentSick = Policy("Parent sick", _sick);
        Assign(parentSick, LeavePolicyTargetKinds.Department, _parentDepartmentId);

        var resolved = await Sut().ResolveAsync(TenantId, _employeeId, _sick, Today, CancellationToken.None);

        resolved!.PolicyId.Should().Be(parentSick);
        resolved.SourceKind.Should().Be(LeavePolicyTargetKinds.Department);
        resolved.SourceTargetId.Should().Be(_parentDepartmentId);
    }

    [Fact]
    public async Task Nearest_department_wins_over_parent()
    {
        var parentSick = Policy("Parent sick", _sick);
        var childSick = Policy("Child sick", _sick);
        Assign(parentSick, LeavePolicyTargetKinds.Department, _parentDepartmentId);
        Assign(childSick, LeavePolicyTargetKinds.Department, _departmentId);

        var resolved = await Sut().ResolveAsync(TenantId, _employeeId, _sick, Today, CancellationToken.None);

        resolved!.PolicyId.Should().Be(childSick);
    }

    [Fact]
    public async Task Department_beats_company()
    {
        var companySick = Policy("Company sick", _sick);
        var departmentSick = Policy("Department sick", _sick);
        Assign(companySick, LeavePolicyTargetKinds.LegalEntity, _legalEntityId);
        Assign(departmentSick, LeavePolicyTargetKinds.Department, _departmentId);

        var resolved = await Sut().ResolveAsync(TenantId, _employeeId, _sick, Today, CancellationToken.None);

        resolved!.PolicyId.Should().Be(departmentSick);
    }

    [Fact]
    public async Task Ended_position_falls_through_to_department()
    {
        _positions.Clear();
        _positions.Add(new LeaveResolutionPosition(_employeeId, _positionId, new DateOnly(2024, 1, 1), new DateOnly(2026, 6, 30)));
        var positionSick = Policy("Position sick", _sick);
        var departmentSick = Policy("Department sick", _sick);
        Assign(positionSick, LeavePolicyTargetKinds.Position, _positionId);
        Assign(departmentSick, LeavePolicyTargetKinds.Department, _departmentId);

        var resolved = await Sut().ResolveAsync(TenantId, _employeeId, _sick, Today, CancellationToken.None);

        resolved!.PolicyId.Should().Be(departmentSick);
    }

    [Fact]
    public async Task Employee_without_department_falls_through_to_company()
    {
        _employeeDepartmentId = null;
        var companySick = Policy("Company sick", _sick);
        Assign(companySick, LeavePolicyTargetKinds.LegalEntity, _legalEntityId);

        var resolved = await Sut().ResolveAsync(TenantId, _employeeId, _sick, Today, CancellationToken.None);

        resolved!.PolicyId.Should().Be(companySick);
    }

    [Fact]
    public async Task No_assignment_returns_null()
    {
        Policy("Unassigned sick", _sick);

        var resolved = await Sut().ResolveAsync(TenantId, _employeeId, _sick, Today, CancellationToken.None);

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task Version_switches_on_effective_date()
    {
        var sick = Policy("Sick", _sick, amountHours: 40m);
        AddVersion(sick, 2, new DateOnly(2026, 11, 1), amountHours: 56m);
        Assign(sick, LeavePolicyTargetKinds.LegalEntity, _legalEntityId);

        var before = await Sut().ResolveAsync(TenantId, _employeeId, _sick, new DateOnly(2026, 10, 31), CancellationToken.None);
        var on = await Sut().ResolveAsync(TenantId, _employeeId, _sick, new DateOnly(2026, 11, 1), CancellationToken.None);

        before!.Version.AmountHours.Should().Be(40m);
        on!.Version.AmountHours.Should().Be(56m);
        on.Version.VersionNumber.Should().Be(2);
    }

    [Fact]
    public async Task Policy_with_no_version_in_force_yet_does_not_resolve()
    {
        var future = Policy("Future sick", _sick, effectiveFrom: new DateOnly(2026, 12, 1));
        Assign(future, LeavePolicyTargetKinds.LegalEntity, _legalEntityId);

        var resolved = await Sut().ResolveAsync(TenantId, _employeeId, _sick, Today, CancellationToken.None);

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task Assignment_resolves_only_inside_its_dates()
    {
        var positionSick = Policy("Position sick", _sick);
        var mySick = Policy("My sick", _sick);
        Assign(positionSick, LeavePolicyTargetKinds.Position, _positionId);
        Assign(mySick, LeavePolicyTargetKinds.Employee, _employeeId,
            from: new DateOnly(2026, 10, 15), to: new DateOnly(2026, 10, 31));

        var dayBefore = await Sut().ResolveAsync(TenantId, _employeeId, _sick, new DateOnly(2026, 10, 14), CancellationToken.None);
        var firstDay = await Sut().ResolveAsync(TenantId, _employeeId, _sick, new DateOnly(2026, 10, 15), CancellationToken.None);
        var afterEnd = await Sut().ResolveAsync(TenantId, _employeeId, _sick, new DateOnly(2026, 11, 1), CancellationToken.None);

        dayBefore!.PolicyId.Should().Be(positionSick);
        firstDay!.PolicyId.Should().Be(mySick);
        afterEnd!.PolicyId.Should().Be(positionSick);
    }

    [Fact]
    public async Task Inactive_leave_type_does_not_resolve()
    {
        // The repository only returns policies whose policy and leave type are both active,
        // so a policy missing from the snapshot never resolves even when assigned.
        var sick = Guid.NewGuid();
        _assignments.Add(Assignment(sick, _sick, LeavePolicyTargetKinds.LegalEntity, _legalEntityId, new DateOnly(2026, 1, 1), null));

        var resolved = await Sut().ResolveAsync(TenantId, _employeeId, _sick, Today, CancellationToken.None);

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task Batch_resolution_matches_single_resolution()
    {
        var other = Guid.NewGuid();
        var companySick = Policy("Company sick", _sick);
        var mySick = Policy("My sick", _sick);
        Assign(companySick, LeavePolicyTargetKinds.LegalEntity, _legalEntityId);
        Assign(mySick, LeavePolicyTargetKinds.Employee, _employeeId);
        var extraEmployees = new[] { new LeaveResolutionEmployee(other, "Other", _legalEntityId, null) };

        var batch = await Sut(extraEmployees).ResolveForEmployeesAsync(TenantId, [_employeeId, other], Today, CancellationToken.None);

        batch[_employeeId].Single().PolicyId.Should().Be(mySick);
        batch[other].Single().PolicyId.Should().Be(companySick);
    }

    [Fact]
    public async Task Resolved_policy_carries_source_name_and_blackouts()
    {
        var sick = Policy("Company sick", _sick);
        Assign(sick, LeavePolicyTargetKinds.LegalEntity, _legalEntityId);
        var version = _versions.Single(v => v.LeavePolicyId == sick);
        var blackout = new LeavePolicyBlackoutPeriod
        {
            Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyVersionId = version.Id,
            StartDate = new DateOnly(2026, 12, 20), EndDate = new DateOnly(2026, 12, 31)
        };

        var resolved = await Sut(blackouts: [blackout]).ResolveAsync(TenantId, _employeeId, _sick, Today, CancellationToken.None);

        resolved!.SourceTargetName.Should().Be("Dapi Technologies");
        resolved.PolicyName.Should().Be("Company sick");
        resolved.Blackouts.Should().ContainSingle().Which.Id.Should().Be(blackout.Id);
    }

    private LeavePolicyResolver Sut(
        IReadOnlyList<LeaveResolutionEmployee>? extraEmployees = null,
        IReadOnlyList<LeavePolicyBlackoutPeriod>? blackouts = null)
    {
        var employees = new List<LeaveResolutionEmployee>
        {
            new(_employeeId, "Priya Nair", _legalEntityId, _employeeDepartmentId)
        };
        employees.AddRange(extraEmployees ?? []);
        var names = new Dictionary<(string Kind, Guid Id), string>
        {
            [(LeavePolicyTargetKinds.LegalEntity, _legalEntityId)] = "Dapi Technologies",
            [(LeavePolicyTargetKinds.Department, _departmentId)] = "Engineering",
            [(LeavePolicyTargetKinds.Department, _parentDepartmentId)] = "Technology",
            [(LeavePolicyTargetKinds.Position, _positionId)] = "Developer",
            [(LeavePolicyTargetKinds.Employee, _employeeId)] = "Priya Nair",
        };
        var snapshot = new LeavePolicyResolutionSnapshot(
            employees, _positions, _departments, _policies, _versions, blackouts ?? [], _assignments, names);
        return new LeavePolicyResolver(new FakeRepository(snapshot));
    }

    private Guid Policy(string name, Guid leaveTypeId, decimal amountHours = 40m, DateOnly? effectiveFrom = null)
    {
        var id = Guid.NewGuid();
        _policies.Add(new LeaveResolutionPolicy(id, name, leaveTypeId));
        AddVersion(id, 1, effectiveFrom ?? new DateOnly(2026, 1, 1), amountHours);
        return id;
    }

    private void AddVersion(Guid policyId, int number, DateOnly effectiveFrom, decimal amountHours) =>
        _versions.Add(new LeavePolicyVersion
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            LeavePolicyId = policyId,
            VersionNumber = number,
            EffectiveFrom = effectiveFrom,
            AmountHours = amountHours,
            AmountPeriod = LeaveAmountPeriods.Year,
        });

    private void Assign(Guid policyId, string kind, Guid targetId, DateOnly? from = null, DateOnly? to = null)
    {
        var leaveTypeId = _policies.Single(p => p.Id == policyId).LeaveTypeId;
        _assignments.Add(Assignment(policyId, leaveTypeId, kind, targetId, from ?? new DateOnly(2026, 1, 1), to));
    }

    private static LeavePolicyAssignment Assignment(
        Guid policyId, Guid leaveTypeId, string kind, Guid targetId, DateOnly from, DateOnly? to) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        LeavePolicyId = policyId,
        LeaveTypeId = leaveTypeId,
        TargetKind = kind,
        TargetId = targetId,
        EffectiveFrom = from,
        EffectiveTo = to,
    };

    private sealed class FakeRepository(LeavePolicyResolutionSnapshot snapshot) : ILeavePolicyResolutionRepository
    {
        public Task<LeavePolicyResolutionSnapshot> LoadAsync(
            Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct) =>
            Task.FromResult(snapshot with
            {
                Employees = snapshot.Employees.Where(e => employeeIds.Contains(e.Id)).ToList()
            });
    }
}
