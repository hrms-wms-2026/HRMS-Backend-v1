using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.Commands.AckTrayNotification;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Notifications;

public class AckTrayNotificationCommandHandlerTests
{
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITrayEmployeeIdentityResolver> _employeeIdentity = new();
    private readonly FakeDateTimeProvider _clock = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _notificationId = Guid.NewGuid();

    public AckTrayNotificationCommandHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);

        // The resolved real Employee.Id is what alert-evaluation jobs store on
        // Notification.EmployeeId - distinct from the raw UserId so tests can tell whether the
        // handler matched ownership by the resolved value or the JWT identity.
        _employeeIdentity.Setup(r => r.ResolveEmployeeIdAsync(
                _tenantId, _userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_employeeId);
    }

    private AckTrayNotificationCommandHandler CreateSut() =>
        new(_notifications.Object, _device.Object, _employeeIdentity.Object, _clock);

    private Notification MakeNotification(Guid employeeId) => new()
    {
        Id = _notificationId, TenantId = _tenantId, EmployeeId = employeeId,
        Type = NotificationType.BreakReminder, Title = "Time for a break", Message = "msg",
        CreatedAt = _clock.UtcNow.AddMinutes(-1)
    };

    [Fact]
    public async Task Handle_OwnedNotification_SetsDeliveredToTrayAt()
    {
        var notification = MakeNotification(_employeeId);
        _notifications.Setup(r => r.GetByIdAsync(_tenantId, _notificationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        var result = await CreateSut().Handle(new AckTrayNotificationCommand(_notificationId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        notification.DeliveredToTrayAt.Should().Be(_clock.UtcNow);
        _notifications.Verify(r => r.Update(notification), Times.Once);
        _notifications.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotificationOwnedByRawUserId_NotResolvedEmployeeId_ReturnsNotFound()
    {
        // Guards against reverting to the pre-fix behaviour: a notification stored under the raw
        // UserId (never written by any alert-evaluation job post-fix) must not match.
        var notification = MakeNotification(_userId);
        _notifications.Setup(r => r.GetByIdAsync(_tenantId, _notificationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        var result = await CreateSut().Handle(new AckTrayNotificationCommand(_notificationId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _notifications.Verify(r => r.Update(It.IsAny<Notification>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NotificationNotFound_ReturnsNotFound()
    {
        _notifications.Setup(r => r.GetByIdAsync(_tenantId, _notificationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Notification?)null);

        var result = await CreateSut().Handle(new AckTrayNotificationCommand(_notificationId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsUnauthorized()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(new AckTrayNotificationCommand(_notificationId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }
}
