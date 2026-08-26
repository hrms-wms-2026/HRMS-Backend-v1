using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Balance.Queries.ListTeamBalances;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Balance;

public class ListTeamBalancesQueryHandlerTests
{
    [Fact]
    public async Task Handle_FiltersToDirectAndIndirectReports()
    {
        var tenantId = Guid.NewGuid();
        var managerUserId = Guid.NewGuid();
        var manager = new Employee { Id = Guid.NewGuid(), TenantId = tenantId, UserId = managerUserId, FirstName = "Mgr", LastName = "One", EmployeeNumber = "M1", HireDate = new DateOnly(2020, 1, 1) };
        var reportId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var reportEntitlement = new LeaveEntitlement
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = reportId, LeaveTypeId = Guid.NewGuid(),
            Year = 2026, TotalDays = 8m, Source = LeaveEntitlementSources.Auto
        };

        var currentUser = new Mock<ICurrentUser>();
        var dateTime = new Mock<IDateTimeProvider>();
        var employees = new Mock<IEmployeeRepository>();
        var hierarchy = new Mock<IEmployeeHierarchyClosureRepository>();
        var entitlements = new Mock<ILeaveEntitlementRepository>();
        var policies = new Mock<ILeavePolicyRepository>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        currentUser.SetupGet(x => x.UserId).Returns(managerUserId);
        dateTime.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero));
        employees.Setup(x => x.GetByUserIdAsync(tenantId, managerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(manager);
        hierarchy.Setup(x => x.GetDescendantEmployeeIdsAsync(tenantId, manager.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([reportId]);
        entitlements.Setup(x => x.ListRowsAsync(
                tenantId,
                It.Is<LeaveEntitlementListFilter>(f => f.Year == 2026 && f.EmployeeIds != null && f.EmployeeIds.Contains(reportId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveEntitlementRow(reportEntitlement, "E2", "Report", null, null, null, null, "Annual", "AL", 4m)]);
        policies.Setup(x => x.ListActiveAggregatesByLegalEntityIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, LeavePolicyAggregate>());

        var handler = new ListTeamBalancesQueryHandler(
            currentUser.Object, dateTime.Object, employees.Object, hierarchy.Object, entitlements.Object, policies.Object);

        var result = await handler.Handle(new ListTeamBalancesQuery(2026, null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value![0].EmployeeId.Should().Be(reportId);
        otherId.Should().NotBe(reportId);
    }
}
