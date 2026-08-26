using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Balance.Queries.GetMyBalances;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Balance;

public class GetMyBalancesQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsOnlyCurrentEmployeeBalances()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employee = new Employee { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, FirstName = "Priya", LastName = "Nair", EmployeeNumber = "E1", HireDate = new DateOnly(2024, 1, 1) };
        var entitlement = new LeaveEntitlement
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id, LeaveTypeId = Guid.NewGuid(),
            Year = 2026, TotalDays = 10m, CarriedForwardDays = 0m, UsedDays = 3m, PendingDays = 0m,
            Source = LeaveEntitlementSources.Auto
        };

        var currentUser = new Mock<ICurrentUser>();
        var dateTime = new Mock<IDateTimeProvider>();
        var employees = new Mock<IEmployeeRepository>();
        var entitlements = new Mock<ILeaveEntitlementRepository>();
        var policies = new Mock<ILeavePolicyRepository>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        currentUser.SetupGet(x => x.UserId).Returns(userId);
        dateTime.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero));
        employees.Setup(x => x.GetByUserIdAsync(tenantId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        entitlements.Setup(x => x.ListRowsAsync(
                tenantId,
                It.Is<LeaveEntitlementListFilter>(f => f.EmployeeId == employee.Id && f.Year == 2026),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveEntitlementRow(entitlement, "E1", "Priya Nair", null, null, null, null, "Annual", "AL", 7m)]);
        policies.Setup(x => x.ListActiveAggregatesByLegalEntityIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, LeavePolicyAggregate>());

        var handler = new GetMyBalancesQueryHandler(
            currentUser.Object, dateTime.Object, employees.Object, entitlements.Object, policies.Object);

        var result = await handler.Handle(new GetMyBalancesQuery(2026), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(x => x.RemainingDays == 7m);
    }
}
