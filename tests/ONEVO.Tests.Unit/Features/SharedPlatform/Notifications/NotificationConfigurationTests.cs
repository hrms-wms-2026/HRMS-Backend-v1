using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

public class NotificationConfigurationTests
{
    [Fact]
    public void Notification_DefaultsToUnread()
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), RecipientUserId = Guid.NewGuid(),
            TemplateCode = "work_task_creation_request_created", Title = "t", Body = "b", CreatedAt = DateTimeOffset.UtcNow
        };

        Assert.False(notification.IsRead);
        Assert.Null(notification.ReadAt);
    }

    [Fact]
    public void NotificationTemplate_DefaultsToInAppEnabledMailDisabled()
    {
        var template = new NotificationTemplate
        {
            Id = Guid.NewGuid(), Code = "work_task_creation_request_created",
            InAppTitleTemplate = "New task request", InAppBodyTemplate = "{{requesterName}} requested a task."
        };

        Assert.True(template.InAppEnabled);
        Assert.False(template.MailEnabled);
    }
}
