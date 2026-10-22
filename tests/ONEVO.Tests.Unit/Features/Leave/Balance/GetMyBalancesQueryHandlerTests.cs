using ONEVO.Application.Features.Leave.Policy.Services;
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Balance.Queries.GetMyBalances;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
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
            Year = 2026, TotalHours = 10m, CarriedForwardHours = 0m, UsedHours = 3m, PendingHours = 0m,
            Source = LeaveEntitlementSources.Auto
        };

        var currentUser = new Mock<ICurrentUser>();
        var dateTime = new Mock<IDateTimeProvider>();
        var employees = new Mock<IEmployeeRepository>();
        var entitlements = new Mock<ILeaveEntitlementRepository>();
        var policies = new Mock<ILeavePolicyResolver>();
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
        policies.Setup(x => x.ResolveForEmployeesAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<ResolvedLeavePolicy>>());

        var handler = new GetMyBalancesQueryHandler(
            currentUser.Object, dateTime.Object, employees.Object, entitlements.Object, policies.Object);

        var result = await handler.Handle(new GetMyBalancesQuery(2026), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(x => x.RemainingHours == 7m);
    }

    [Fact]
    public async Task Handle_includes_resolved_type_at_zero_and_keeps_old_unresolved_negative_balance()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employee = new Employee
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId,
            FirstName = "Priya", LastName = "Nair", EmployeeNumber = "E1",
            HireDate = new DateOnly(2024, 1, 1)
        };
        var oldTypeId = Guid.NewGuid();
        var resolvedTypeId = Guid.NewGuid();
        var oldEntitlement = new LeaveEntitlement
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id,
            LeaveTypeId = oldTypeId, Year = 2026, TotalHours = 8m, UsedHours = 12m,
            Source = LeaveEntitlementSources.Auto
        };
        var resolved = LeaveTestData.CreateResolvedPolicy(tenantId, resolvedTypeId, Guid.NewGuid());
        resolved.Version.AmountHours = 8m;
        resolved.Version.AmountPeriod = LeaveAmountPeriods.Month;

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        currentUser.SetupGet(x => x.UserId).Returns(userId);
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero));
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetByUserIdAsync(tenantId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        employees.Setup(x => x.ListByIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee> { [employee.Id] = employee });
        var entitlements = new Mock<ILeaveEntitlementRepository>();
        entitlements.Setup(x => x.ListRowsAsync(tenantId, It.IsAny<LeaveEntitlementListFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveEntitlementRow(
                oldEntitlement, "E1", "Priya Nair", null, null, null, null, "Legacy", "LEG", -4m)]);
        var policies = new Mock<ILeavePolicyResolver>();
        policies.Setup(x => x.ResolveForEmployeesAsync(
                tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<ResolvedLeavePolicy>> { [employee.Id] = [resolved] });
        var leaveTypes = new Mock<ILeaveTypeRepository>();
        leaveTypes.Setup(x => x.ListAsync(tenantId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LeaveType
            {
                Id = resolvedTypeId, TenantId = tenantId, Name = "Annual", Code = "AL", IsActive = true
            }]);
        var handler = new GetMyBalancesQueryHandler(
            currentUser.Object, clock.Object, employees.Object, entitlements.Object, policies.Object, leaveTypes.Object);

        var result = await handler.Handle(new GetMyBalancesQuery(2026), default);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Should().HaveCount(2);
        result.Value.Should().Contain(x => x.LeaveTypeId == oldTypeId && x.RemainingHours == -4m
            && x.IsNegative && x.PolicyName == null);
        result.Value.Should().Contain(x => x.LeaveTypeId == resolvedTypeId && x.RemainingHours == 0m
            && x.PolicyName == resolved.PolicyName && x.AmountHours == 8m && x.AmountPeriod == LeaveAmountPeriods.Month);
    }
}
