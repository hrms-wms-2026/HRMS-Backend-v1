using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.Queries.GetNotificationInbox;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.Notifications.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Notifications;

public class GetNotificationInboxQueryHandlerTests
{
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IEmployeeRepository> _employees = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public GetNotificationInboxQueryHandlerTests()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.TenantId).Returns(_tenantId);
        _currentUser.Setup(u => u.UserId).Returns(_userId);

        // The resolved real Employee.Id is what the alert-evaluation jobs store on
        // Notification.EmployeeId - distinct from the raw session UserId so tests can tell whether
        // the handler queried the repository by the resolved value or the raw identity.
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = _employeeId, UserId = _userId, TenantId = _tenantId });
    }

    private GetNotificationInboxQueryHandler CreateSut() =>
        new(_notifications.Object, _currentUser.Object, _employees.Object);

    [Fact]
    public async Task Handle_QueriesRepositoryByResolvedEmployeeId_NotRawUserId()
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, EmployeeId = _employeeId,
            Type = NotificationType.BreakReminder, Title = "Time for a break", Message = "msg",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Pinned to the literal resolved EmployeeId (not It.IsAny) so a regression that passes the
        // raw UserId back through would leave these unmatched and the mocks would return their
        // default (empty list / 0), failing the assertions below rather than passing vacuously.
        _notifications.Setup(r => r.GetInboxAsync(_tenantId, _employeeId, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([notification]);
        _notifications.Setup(r => r.GetInboxTotalCountAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await CreateSut().Handle(new GetNotificationInboxQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle(i => i.Id == notification.Id);
        _notifications.Verify(
            r => r.GetInboxAsync(_tenantId, _userId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _notifications.Verify(
            r => r.GetInboxTotalCountAsync(_tenantId, _userId, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NoEmployeeRecord_ReturnsEmptyPageWithoutQueryingNotifications()
    {
        _employees.Setup(e => e.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);

        var result = await CreateSut().Handle(new GetNotificationInboxQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(0);
        result.Value.Items.Should().BeEmpty();
        _notifications.Verify(
            r => r.GetInboxAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(new GetNotificationInboxQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
}
