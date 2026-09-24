using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure.Services.SharedPlatform.Notifications;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

public class NotificationDispatcherTests
{
    [Fact]
    public async Task SendTemplatedAsync_RendersPlaceholdersAndWritesNotification()
    {
        var tenantId = Guid.NewGuid();
        var recipientUserId = Guid.NewGuid();

        var repo = new Mock<INotificationRepository>();
        repo.Setup(x => x.GetTemplateByCodeAsync("work_task_creation_request_created", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationTemplate
            {
                Id = Guid.NewGuid(), Code = "work_task_creation_request_created",
                InAppTitleTemplate = "New task request",
                InAppBodyTemplate = "{{requesterName}} requested \"{{taskTitle}}\".",
                InAppEnabled = true, MailEnabled = false
            });

        var dispatcher = new NotificationDispatcher(repo.Object);
        await dispatcher.SendTemplatedAsync(
            tenantId, recipientUserId, "work_task_creation_request_created",
            new Dictionary<string, string> { ["requesterName"] = "Priya", ["taskTitle"] = "Build the thing" },
            "task_creation_request", Guid.NewGuid());

        repo.Verify(x => x.AddAsync(
            It.Is<Notification>(n => n.Body == "Priya requested \"Build the thing\"." && n.TenantId == tenantId && n.RecipientUserId == recipientUserId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendTemplatedAsync_TemplateInAppDisabled_DoesNotWriteNotification()
    {
        var repo = new Mock<INotificationRepository>();
        repo.Setup(x => x.GetTemplateByCodeAsync("some_code", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationTemplate
            {
                Id = Guid.NewGuid(), Code = "some_code",
                InAppTitleTemplate = "t", InAppBodyTemplate = "b", InAppEnabled = false
            });

        var dispatcher = new NotificationDispatcher(repo.Object);
        await dispatcher.SendTemplatedAsync(Guid.NewGuid(), Guid.NewGuid(), "some_code", new Dictionary<string, string>());

        repo.Verify(x => x.AddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
