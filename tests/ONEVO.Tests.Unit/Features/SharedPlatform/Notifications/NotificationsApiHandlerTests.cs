using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Commands.MarkAllNotificationsRead;
using ONEVO.Application.Features.SharedPlatform.Notifications.Commands.MarkNotificationRead;
using ONEVO.Application.Features.SharedPlatform.Notifications.Queries.GetMyNotifications;
using ONEVO.Application.Features.SharedPlatform.Notifications.Queries.GetUnreadCount;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

public class NotificationsApiHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static Mock<ICurrentUser> AuthUser()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);
        return currentUser;
    }

    [Fact]
    public async Task GetMyNotifications_ReturnsMappedItems()
    {
        var repo = new Mock<INotificationRepository>();
        repo.Setup(x => x.GetByRecipientAsync(TenantId, UserId, false, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Notification>
            {
                new()
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, RecipientUserId = UserId,
                    TemplateCode = "work_task_creation_request_created", Title = "New task request",
                    Body = "Priya requested a task.", CreatedAt = DateTimeOffset.UtcNow
                }
            });

        var handler = new GetMyNotificationsQueryHandler(AuthUser().Object, repo.Object);
        var result = await handler.Handle(new GetMyNotificationsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("New task request", result.Value![0].Title);
    }

    [Fact]
    public async Task GetUnreadCount_ReturnsCount()
    {
        var repo = new Mock<INotificationRepository>();
        repo.Setup(x => x.GetUnreadCountAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var handler = new GetUnreadCountQueryHandler(AuthUser().Object, repo.Object);
        var result = await handler.Handle(new GetUnreadCountQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);
    }

    [Fact]
    public async Task MarkNotificationRead_MarksTrackedRow()
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(), TenantId = TenantId, RecipientUserId = UserId,
            TemplateCode = "x", Title = "t", Body = "b", IsRead = false, CreatedAt = DateTimeOffset.UtcNow
        };
        var repo = new Mock<INotificationRepository>();
        repo.Setup(x => x.GetTrackedByIdForRecipientAsync(TenantId, notification.Id, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new MarkNotificationReadCommandHandler(AuthUser().Object, repo.Object, unitOfWork.Object);
        var result = await handler.Handle(new MarkNotificationReadCommand(notification.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(notification.IsRead);
        Assert.NotNull(notification.ReadAt);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkAllNotificationsRead_CallsRepository()
    {
        var repo = new Mock<INotificationRepository>();
        var handler = new MarkAllNotificationsReadCommandHandler(AuthUser().Object, repo.Object);

        var result = await handler.Handle(new MarkAllNotificationsReadCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(x => x.MarkAllReadAsync(TenantId, UserId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
