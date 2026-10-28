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

public class EmployeeClockInPolicyResolverBatchTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly Guid DepartmentId = Guid.NewGuid();
    private static readonly Guid PositionId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 19);

    private static readonly Employee ByEmployeeScope = new() { Id = Guid.NewGuid(), TenantId = TenantId };
    private static readonly Employee ByPosition = new() { Id = Guid.NewGuid(), TenantId = TenantId };
    private static readonly Employee ByDepartment = new() { Id = Guid.NewGuid(), TenantId = TenantId, DepartmentId = DepartmentId };
    private static readonly Employee ByCompany = new() { Id = Guid.NewGuid(), TenantId = TenantId };
    private static readonly Employee[] All = [ByEmployeeScope, ByPosition, ByDepartment, ByCompany];

    private readonly Mock<IClockInPolicyRepository> _policies = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IPositionAssignmentRepository> _assignments = new();

    public EmployeeClockInPolicyResolverBatchTests()
    {
        List<ClockInPolicy> active =
        [
            Policy(ClockInPolicy.ScopeEmployee, [ByEmployeeScope.Id], 5),
            Policy(ClockInPolicy.ScopePosition, [PositionId], 10),
            Policy(ClockInPolicy.ScopeDepartment, [DepartmentId], 15),
            Policy(ClockInPolicy.ScopeFullCompany, null, 20),
        ];
        _policies.Setup(p => p.ListActiveOnDateAsync(TenantId, LegalEntityId, Day, It.IsAny<CancellationToken>()))
            .ReturnsAsync(active);
        _assignments.Setup(a => a.GetActivePrimaryByEmployeeIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PositionAssignment>
            {
                [ByPosition.Id] = new() { EmployeeId = ByPosition.Id, PositionId = PositionId },
            });
        foreach (var e in All)
        {
            _employees.Setup(r => r.GetByIdAsync(TenantId, e.Id, It.IsAny<CancellationToken>())).ReturnsAsync(e);
            _assignments.Setup(a => a.GetActivePrimaryAsync(TenantId, e.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(e == ByPosition ? new PositionAssignment { EmployeeId = e.Id, PositionId = PositionId } : null);
        }
    }

    private EmployeeClockInPolicyResolver Resolver() => new(_policies.Object, _employees.Object, _assignments.Object);

    private static ClockInPolicy Policy(string scope, Guid[]? ids, int thresholdMinutes)
    {
        var policy = new ClockInPolicy
        {
            Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId, Name = scope, ScopeType = scope, IsActive = true,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            DepartmentIds = scope == ClockInPolicy.ScopeDepartment ? ids : null,
            PositionIds = scope == ClockInPolicy.ScopePosition ? ids : null,
            EmployeeIds = scope == ClockInPolicy.ScopeEmployee ? ids : null,
        };
        policy.LateDeductionRules.Add(new ClockInLateDeductionRule
        {
            Id = Guid.NewGuid(), ClockInPolicyId = policy.Id, LateArrivalMinute = thresholdMinutes, Multiplier = 1,
            TimeOffTypeId = Guid.NewGuid(), IsActive = true,
        });
        return policy;
    }

    [Fact]
    public async Task ResolveThresholds_MatchesSingleResolvePerEmployee()
    {
        var resolver = Resolver();
        var batch = await resolver.ResolveThresholdsAsync(TenantId, LegalEntityId, All, Day, CancellationToken.None);

        foreach (var e in All)
        {
            var single = ClockInPolicyRules.ThresholdMinutes(await resolver.ResolveAsync(TenantId, e.Id, LegalEntityId, Day, CancellationToken.None));
            batch[e.Id].Should().Be(single);
        }
        batch[ByEmployeeScope.Id].Should().Be(5);
        batch[ByPosition.Id].Should().Be(10);
        batch[ByDepartment.Id].Should().Be(15);
        batch[ByCompany.Id].Should().Be(20);
    }

    [Fact]
    public async Task ResolveThresholds_IssuesOnePolicyAndOneAssignmentQuery()
    {
        await Resolver().ResolveThresholdsAsync(TenantId, LegalEntityId, All, Day, CancellationToken.None);

        _policies.Verify(p => p.ListActiveOnDateAsync(TenantId, LegalEntityId, Day, It.IsAny<CancellationToken>()), Times.Once);
        _assignments.Verify(a => a.GetActivePrimaryByEmployeeIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        _assignments.Verify(a => a.GetActivePrimaryAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _employees.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveThresholds_NoActivePolicies_AllZero()
    {
        _policies.Setup(p => p.ListActiveOnDateAsync(TenantId, LegalEntityId, Day, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClockInPolicy>());

        var batch = await Resolver().ResolveThresholdsAsync(TenantId, LegalEntityId, All, Day, CancellationToken.None);

        batch.Values.Should().AllSatisfy(v => v.Should().Be(0));
        batch.Should().HaveCount(4);
    }
}
