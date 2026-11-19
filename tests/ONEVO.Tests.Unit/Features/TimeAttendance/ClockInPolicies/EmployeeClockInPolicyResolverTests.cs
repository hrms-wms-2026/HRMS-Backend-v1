using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.TimeAttendance.ClockInPolicies;

public class EmployeeClockInPolicyResolverTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid DepartmentId = Guid.NewGuid();
    private static readonly Guid PositionId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 19);

    private readonly Mock<IClockInPolicyRepository> _policies = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IPositionAssignmentRepository> _assignments = new();
    private readonly List<ClockInPolicy> _active = [];

    public EmployeeClockInPolicyResolverTests()
    {
        _policies.Setup(p => p.ListActiveOnDateAsync(TenantId, LegalEntityId, Day, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _active.ToList());
        _employees.Setup(e => e.GetByIdAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, TenantId = TenantId, DepartmentId = DepartmentId });
        _assignments.Setup(a => a.GetActivePrimaryAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionAssignment { EmployeeId = EmployeeId, PositionId = PositionId });
    }

    private EmployeeClockInPolicyResolver Resolver() => new(_policies.Object, _employees.Object, _assignments.Object);

    private static ClockInPolicy Policy(string name, string scope, Guid[]? ids = null, params (int Minutes, decimal Multiplier)[] rows)
    {
        var policy = new ClockInPolicy
        {
            Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId, Name = name, ScopeType = scope, IsActive = true,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            DepartmentIds = scope == ClockInPolicy.ScopeDepartment ? ids : null,
            PositionIds = scope == ClockInPolicy.ScopePosition ? ids : null,
            EmployeeIds = scope == ClockInPolicy.ScopeEmployee ? ids : null,
        };
        foreach (var (minutes, multiplier) in rows)
            policy.LateDeductionRules.Add(new ClockInLateDeductionRule
            {
                Id = Guid.NewGuid(), ClockInPolicyId = policy.Id, LateArrivalMinute = minutes, Multiplier = multiplier,
                TimeOffTypeId = Guid.NewGuid(), IsActive = true,
            });
        return policy;
    }

    [Fact]
    public async Task Resolve_PrefersEmployeeOverPositionOverDepartmentOverCompany()
    {
        var company = Policy("company", ClockInPolicy.ScopeFullCompany);
        var department = Policy("department", ClockInPolicy.ScopeDepartment, [DepartmentId]);
        var position = Policy("position", ClockInPolicy.ScopePosition, [PositionId]);
        var employee = Policy("employee", ClockInPolicy.ScopeEmployee, [EmployeeId]);
        _active.AddRange([company, department, position, employee]);

        (await Resolver().ResolveAsync(TenantId, EmployeeId, LegalEntityId, Day, default))!.Name.Should().Be("employee");
        _active.Remove(employee);
        (await Resolver().ResolveAsync(TenantId, EmployeeId, LegalEntityId, Day, default))!.Name.Should().Be("position");
        _active.Remove(position);
        (await Resolver().ResolveAsync(TenantId, EmployeeId, LegalEntityId, Day, default))!.Name.Should().Be("department");
        _active.Remove(department);
        (await Resolver().ResolveAsync(TenantId, EmployeeId, LegalEntityId, Day, default))!.Name.Should().Be("company");
    }

    [Fact]
    public async Task Resolve_SkipsPoliciesThatDoNotCoverTheEmployee()
    {
        _active.Add(Policy("other employee", ClockInPolicy.ScopeEmployee, [Guid.NewGuid()]));
        _active.Add(Policy("other department", ClockInPolicy.ScopeDepartment, [Guid.NewGuid()]));

        (await Resolver().ResolveAsync(TenantId, EmployeeId, LegalEntityId, Day, default)).Should().BeNull();
    }

    [Fact]
    public async Task Resolve_ReturnsNullWithoutPolicy()
        => (await Resolver().ResolveAsync(TenantId, EmployeeId, LegalEntityId, Day, default)).Should().BeNull();

    [Fact]
    public void Match_PicksHighestRowNotAboveMinutes()
    {
        var policy = Policy("p", ClockInPolicy.ScopeFullCompany, null, (60, 2m), (15, 1m));

        ClockInPolicyRules.Match(policy, 10).Should().BeNull();
        ClockInPolicyRules.Match(policy, 15)!.LateArrivalMinute.Should().Be(15);
        ClockInPolicyRules.Match(policy, 30)!.LateArrivalMinute.Should().Be(15);
        ClockInPolicyRules.Match(policy, 75)!.LateArrivalMinute.Should().Be(60);
    }

    [Fact]
    public void ThresholdMinutes_IsFirstActiveRow_OrZero()
    {
        var policy = Policy("p", ClockInPolicy.ScopeFullCompany, null, (60, 2m), (15, 1m));
        policy.LateDeductionRules.First(r => r.LateArrivalMinute == 15).IsActive = false;

        ClockInPolicyRules.ThresholdMinutes(policy).Should().Be(60);
        ClockInPolicyRules.ThresholdMinutes(Policy("none", ClockInPolicy.ScopeFullCompany)).Should().Be(0);
        ClockInPolicyRules.ThresholdMinutes(null).Should().Be(0);
    }
}
