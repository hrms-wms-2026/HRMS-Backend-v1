using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.Commands.MarkNotificationRead;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Notifications;

public class MarkNotificationReadCommandHandlerTests
{
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly FakeDateTimeProvider _clock = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _notificationId = Guid.NewGuid();

    public MarkNotificationReadCommandHandlerTests()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.TenantId).Returns(_tenantId);
        _currentUser.Setup(u => u.UserId).Returns(_userId);

        // The resolved real Employee.Id is what the alert-evaluation jobs store on
        // Notification.EmployeeId - distinct from the raw session UserId so tests can tell whether
        // the handler matched ownership by the resolved value or the raw identity.
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = _employeeId, UserId = _userId, TenantId = _tenantId });
    }

    private MarkNotificationReadCommandHandler CreateSut() =>
        new(_notifications.Object, _currentUser.Object, _employees.Object, _clock);

    private Notification MakeNotification(Guid employeeId) => new()
    {
        Id = _notificationId, TenantId = _tenantId, EmployeeId = employeeId,
        Type = NotificationType.BreakReminder, Title = "Time for a break", Message = "msg",
        CreatedAt = _clock.UtcNow.AddMinutes(-1)
    };

    [Fact]
    public async Task Handle_OwnedNotification_SetsReadAt()
    {
        var notification = MakeNotification(_employeeId);
        _notifications.Setup(r => r.GetByIdAsync(_tenantId, _notificationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        var result = await CreateSut().Handle(new MarkNotificationReadCommand(_notificationId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        notification.ReadAt.Should().Be(_clock.UtcNow);
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

        var result = await CreateSut().Handle(new MarkNotificationReadCommand(_notificationId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _notifications.Verify(r => r.Update(It.IsAny<Notification>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NotificationNotFound_ReturnsNotFound()
    {
        _notifications.Setup(r => r.GetByIdAsync(_tenantId, _notificationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Notification?)null);

        var result = await CreateSut().Handle(new MarkNotificationReadCommand(_notificationId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_NoEmployeeRecord_ReturnsNotFound()
    {
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);

        var result = await CreateSut().Handle(new MarkNotificationReadCommand(_notificationId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _notifications.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(new MarkNotificationReadCommand(_notificationId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
}
