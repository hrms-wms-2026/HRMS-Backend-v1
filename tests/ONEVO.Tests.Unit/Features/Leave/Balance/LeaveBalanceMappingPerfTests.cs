using Moq;
using ONEVO.Application.Features.Leave.Balance.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Balance;

public class LeaveBalanceMappingPerfTests
{
    // Regression guard for the Phase 9 audit finding: LeaveBalanceMapping.MapAsync must call
    // ListActiveAggregatesByLegalEntityIdsAsync exactly once per request, batched over every
    // distinct legal entity in the result set — never once per row. If this test ever fails
    // with Times.AtLeastOnce() succeeding but Times.Once() failing, an N+1 has been
    // reintroduced into this mapper.
    [Fact]
    public async Task MapAsync_BatchesPolicyLookup_RegardlessOfRowCount()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var policiesMock = new Mock<ILeavePolicyRepository>();
        policiesMock
            .Setup(p => p.ListActiveAggregatesByLegalEntityIdsAsync(
                tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, LeavePolicyAggregate>());

        var rows = Enumerable.Range(0, 50).Select(i => new LeaveEntitlementRow(
            new LeaveEntitlement { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = Guid.NewGuid(), LeaveTypeId = leaveTypeId, Year = 2027 },
            $"EMP{i:000}", $"Employee {i}", null, null, legalEntityId, "Acme UK", "Annual Leave", "ANNUAL", 20m
        )).ToList();

        await LeaveBalanceMapping.MapAsync(policiesMock.Object, tenantId, 2027, new DateOnly(2027, 1, 1), rows, CancellationToken.None);

        policiesMock.Verify(p => p.ListActiveAggregatesByLegalEntityIdsAsync(
            tenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(legalEntityId)), 2027, It.IsAny<CancellationToken>()), Times.Once);
    }
}
