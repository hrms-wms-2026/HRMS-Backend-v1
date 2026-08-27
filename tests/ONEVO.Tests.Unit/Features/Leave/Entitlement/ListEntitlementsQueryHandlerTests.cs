using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Queries.ListEntitlements;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Entitlement;

public class ListEntitlementsQueryHandlerTests
{
    [Fact]
    public async Task Handle_EnrichesRowsWithLegalEntityChangeWarning()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var entitlement = new LeaveEntitlement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            LeaveTypeId = Guid.NewGuid(),
            Year = 2026,
            TotalDays = 10m,
            Source = LeaveEntitlementSources.Auto
        };
        var row = new LeaveEntitlementRow(
            entitlement, "EMP-001", "Nila Perera", null, null, Guid.NewGuid(), "Acme", "Annual", "AL", 10m);

        var currentUser = new Mock<ICurrentUser>();
        var dateTime = new Mock<IDateTimeProvider>();
        var entitlements = new Mock<ILeaveEntitlementRepository>();
        var employees = new Mock<IEmployeeRepository>();
        var policies = new Mock<ILeavePolicyRepository>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        dateTime.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero));
        entitlements.Setup(x => x.ListRowsAsync(tenantId, It.IsAny<LeaveEntitlementListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([row]);
        employees.Setup(x => x.ListLegalEntityChangeWarningsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [employeeId] = "Employee changed legal entity on 2026-06-01" });
        policies.Setup(x => x.ListActiveAggregatesByLegalEntityIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, LeavePolicyAggregate>());

        var handler = new ListEntitlementsQueryHandler(
            currentUser.Object, dateTime.Object, entitlements.Object, employees.Object, policies.Object);

        var result = await handler.Handle(new ListEntitlementsQuery(2026, null, null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(x => x.Warning == "Employee changed legal entity on 2026-06-01");
    }
}
