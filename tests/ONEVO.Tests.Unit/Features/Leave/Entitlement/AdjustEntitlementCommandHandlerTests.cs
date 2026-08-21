using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Commands.AdjustEntitlement;
using ONEVO.Application.Features.Leave.Entitlement.Helpers;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Entitlement;

public class AdjustEntitlementCommandHandlerTests
{
    [Fact]
    public async Task Handle_AdjustBelowUsed_RequiresConfirmation()
    {
        var tenantId = Guid.NewGuid();
        var entitlement = CreateEntitlement(tenantId, 4m, 5m, 0m, 0m);
        var currentUser = new Mock<ICurrentUser>();
        var entitlements = new Mock<ILeaveEntitlementRepository>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        entitlements.Setup(x => x.GetTrackedByIdAsync(tenantId, entitlement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entitlement);

        var handler = new AdjustEntitlementCommandHandler(
            currentUser.Object, new Mock<IDateTimeProvider>().Object, entitlements.Object);

        var result = await handler.Handle(
            new AdjustEntitlementCommand(entitlement.Id, 3m, 0m, "Policy correction", false),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Error.Should().Contain("Employee will show negative balance");
    }

    [Fact]
    public async Task Handle_AdjustWithConfirmation_SavesAuditWithDelta()
    {
        var tenantId = Guid.NewGuid();
        var entitlement = CreateEntitlement(tenantId, 10m, 2m, 1m, 0m);
        LeaveBalanceAudit? capturedAudit = null;
        var currentUser = new Mock<ICurrentUser>();
        var dateTime = new Mock<IDateTimeProvider>();
        var entitlements = new Mock<ILeaveEntitlementRepository>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        currentUser.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        dateTime.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero));
        entitlements.Setup(x => x.GetTrackedByIdAsync(tenantId, entitlement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entitlement);
        entitlements.Setup(x => x.SaveWithAuditAsync(entitlement, It.IsAny<LeaveBalanceAudit>(), It.IsAny<CancellationToken>()))
            .Callback<LeaveEntitlement, LeaveBalanceAudit, CancellationToken>((_, audit, _) => capturedAudit = audit)
            .Returns(Task.CompletedTask);
        entitlements.Setup(x => x.GetRowByIdAsync(tenantId, entitlement.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new LeaveEntitlementRow(
                entitlement, "EMP-001", "Anu Raman", null, null, null, null, "Annual", "AL", 10m));

        var handler = new AdjustEntitlementCommandHandler(currentUser.Object, dateTime.Object, entitlements.Object);

        var result = await handler.Handle(
            new AdjustEntitlementCommand(entitlement.Id, 12m, 1m, "Manager correction", false),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedAudit!.ChangeType.Should().Be(LeaveBalanceChangeTypes.Adjustment);
        capturedAudit.DaysChanged.Should().Be(3m);
        capturedAudit.BalanceAfter.Should().Be(10m);
        result.Error.Should().BeNull();
    }

    private static LeaveEntitlement CreateEntitlement(
        Guid tenantId, decimal totalDays, decimal usedDays, decimal pendingDays, decimal carriedForwardDays) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = Guid.NewGuid(),
        LeaveTypeId = Guid.NewGuid(),
        Year = 2026,
        TotalDays = totalDays,
        UsedDays = usedDays,
        PendingDays = pendingDays,
        CarriedForwardDays = carriedForwardDays,
        Source = LeaveEntitlementSources.Auto
    };
}
