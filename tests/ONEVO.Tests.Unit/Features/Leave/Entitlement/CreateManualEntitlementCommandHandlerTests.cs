using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Entitlement.Commands.CreateManualEntitlement;
using ONEVO.Application.Features.Leave.Entitlement.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Entitlement.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Entitlement;

public class CreateManualEntitlementCommandHandlerTests
{
    [Fact]
    public async Task Handle_ManualAssignment_PersistsRequestValuesAndAudit()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        LeaveEntitlement? capturedEntitlement = null;
        LeaveBalanceAudit? capturedAudit = null;

        var currentUser = new Mock<ICurrentUser>();
        var dateTime = new Mock<IDateTimeProvider>();
        var employees = new Mock<IEmployeeRepository>();
        var leaveTypes = new Mock<ILeaveTypeRepository>();
        var entitlements = new Mock<ILeaveEntitlementRepository>();

        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(tenantId);
        currentUser.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        dateTime.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero));
        employees.Setup(x => x.GetByIdAsync(tenantId, employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = employeeId, TenantId = tenantId, FirstName = "Anu", LastName = "Raman", EmployeeNumber = "EMP-001", HireDate = new DateOnly(2024, 1, 1) });
        leaveTypes.Setup(x => x.GetByIdAsync(tenantId, leaveTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaveType { Id = leaveTypeId, TenantId = tenantId, Name = "Study", Code = "ST", IsActive = true });
        entitlements.Setup(x => x.GetTrackedByEmployeeTypeYearAsync(tenantId, employeeId, leaveTypeId, 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeaveEntitlement?)null);
        entitlements.Setup(x => x.AddManualAsync(It.IsAny<LeaveEntitlement>(), It.IsAny<LeaveBalanceAudit>(), It.IsAny<CancellationToken>()))
            .Callback<LeaveEntitlement, LeaveBalanceAudit, CancellationToken>((entitlement, audit, _) =>
            {
                capturedEntitlement = entitlement;
                capturedAudit = audit;
            })
            .Returns(Task.CompletedTask);
        entitlements.Setup(x => x.GetRowByIdAsync(tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new LeaveEntitlementRow(
                capturedEntitlement!, "EMP-001", "Anu Raman", null, null, null, null, "Study", "ST", 15m));

        var handler = new CreateManualEntitlementCommandHandler(
            currentUser.Object, dateTime.Object, employees.Object, leaveTypes.Object, entitlements.Object);

        var result = await handler.Handle(
            new CreateManualEntitlementCommand(employeeId, leaveTypeId, 2026, 13.5m, 1.5m, "Contractual top-up"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedEntitlement!.TotalDays.Should().Be(13.5m);
        capturedEntitlement.CarriedForwardDays.Should().Be(1.5m);
        capturedEntitlement.Source.Should().Be(LeaveEntitlementSources.Manual);
        capturedAudit!.ChangeType.Should().Be(LeaveBalanceChangeTypes.Accrual);
        capturedAudit.Reason.Should().Be("Contractual top-up");
        result.Value!.TotalDays.Should().Be(13.5m);
    }
}
