using FluentAssertions;
using Moq;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.Queries.GetPendingTrayNotifications;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Notifications;

public class GetPendingTrayNotificationsQueryHandlerTests
{
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITrayEmployeeIdentityResolver> _employeeIdentity = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public GetPendingTrayNotificationsQueryHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);

        // The resolved real Employee.Id is what alert-evaluation jobs store on
        // Notification.EmployeeId - distinct from the raw UserId so tests can tell whether the
        // handler queried by the resolved value or the JWT identity.
        _employeeIdentity.Setup(r => r.ResolveEmployeeIdAsync(
                _tenantId, _userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_employeeId);
    }

    private GetPendingTrayNotificationsQueryHandler CreateSut() =>
        new(_notifications.Object, _device.Object, _employeeIdentity.Object);

    [Fact]
    public async Task Handle_ReturnsPendingBreakAndIdleNotifications()
    {
        _notifications.Setup(r => r.GetPendingForTrayAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Notification
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
                Type = NotificationType.BreakReminder, Title = "Time for a break", Message = "msg", CreatedAt = DateTimeOffset.UtcNow
            }]);

        var result = await CreateSut().Handle(new GetPendingTrayNotificationsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(n => n.Title == "Time for a break");
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsUnauthorized()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(new GetPendingTrayNotificationsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }
}
