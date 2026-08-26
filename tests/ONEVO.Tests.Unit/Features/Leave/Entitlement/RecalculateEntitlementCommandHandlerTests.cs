using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Commands.RecalculateEntitlement;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Entitlement;

public class RecalculateEntitlementCommandHandlerTests
{
    [Fact]
    public async Task Handle_Recalculate_UsesCurrentPolicyButKeepsUsedAndPending()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var employee = PreviewGenerateEntitlementsQueryHandlerTests.CreateEmployee(tenantId, legalEntityId);
        var policy = PreviewGenerateEntitlementsQueryHandlerTests.CreatePolicyAggregate(tenantId, legalEntityId, 14m, 0m);
        var leaveTypeId = policy.LeaveTypes.Single().Rule.LeaveTypeId;
        var entitlement = new LeaveEntitlement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employee.Id,
            LeaveTypeId = leaveTypeId,
            Year = 2026,
            TotalDays = 10m,
            UsedDays = 2m,
            PendingDays = 1m,
            CarriedForwardDays = 0m,
            Source = LeaveEntitlementSources.Auto
        };

        var currentUser = new Mock<ICurrentUser>();
        var dateTime = new Mock<IDateTimeProvider>();
        var entitlements = new Mock<ILeaveEntitlementRepository>();
        var employees = new Mock<IEmployeeRepository>();
        var policies = new Mock<ILeavePolicyRepository>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        currentUser.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        dateTime.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));
        entitlements.Setup(x => x.GetTrackedByIdAsync(tenantId, entitlement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entitlement);
        employees.Setup(x => x.GetByIdAsync(tenantId, entitlement.EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        policies.Setup(x => x.ListActiveAggregatesByLegalEntityIdsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, LeavePolicyAggregate> { [legalEntityId] = policy });
        entitlements.Setup(x => x.ListPreviousYearAsync(tenantId, 2025, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<(Guid, Guid), LeaveEntitlement>());
        entitlements.Setup(x => x.SaveWithAuditAsync(entitlement, It.IsAny<ONEVO.Domain.Features.Leave.BalanceAudit.Entities.LeaveBalanceAudit>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        entitlements.Setup(x => x.GetRowByIdAsync(tenantId, entitlement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new LeaveEntitlementRow(
                entitlement, employee.EmployeeNumber, "Priya Nair", null, null, legalEntityId, "Acme", "Annual Leave", "AL", 11m));
        employees.Setup(x => x.ListLegalEntityChangeWarningsAsync(tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());

        var handler = new RecalculateEntitlementCommandHandler(
            currentUser.Object,
            dateTime.Object,
            entitlements.Object,
            employees.Object,
            policies.Object,
            new LeaveEntitlementCalculator(new LeaveWorkingDayCounter()));

        var result = await handler.Handle(new RecalculateEntitlementCommand(entitlement.Id, true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        entitlement.TotalDays.Should().Be(14m);
        entitlement.UsedDays.Should().Be(2m);
        entitlement.PendingDays.Should().Be(1m);
    }
}
