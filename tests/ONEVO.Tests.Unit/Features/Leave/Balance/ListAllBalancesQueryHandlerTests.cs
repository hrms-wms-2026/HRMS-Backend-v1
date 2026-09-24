using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Balance.Queries.ListAllBalances;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Balance;

public class ListAllBalancesQueryHandlerTests
{
    [Fact]
    public async Task Handle_ForwardsAllBalanceFilters()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        var dateTime = new Mock<IDateTimeProvider>();
        var entitlements = new Mock<ILeaveEntitlementRepository>();
        var policies = new Mock<ILeavePolicyRepository>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        dateTime.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero));
        entitlements.Setup(x => x.ListRowsAsync(
                tenantId,
                It.Is<LeaveEntitlementListFilter>(f =>
                    f.Year == 2026 &&
                    f.LegalEntityId == legalEntityId &&
                    f.DepartmentId == departmentId &&
                    f.LeaveTypeId == leaveTypeId &&
                    f.EmploymentStatusId == 1 &&
                    f.Search == "anu"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        policies.Setup(x => x.ListActiveAggregatesByLegalEntityIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, LeavePolicyAggregate>());

        var handler = new ListAllBalancesQueryHandler(
            currentUser.Object, dateTime.Object, entitlements.Object, policies.Object);

        var result = await handler.Handle(
            new ListAllBalancesQuery(2026, legalEntityId, departmentId, leaveTypeId, 1, "anu"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        entitlements.VerifyAll();
    }
}
