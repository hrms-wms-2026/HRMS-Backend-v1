using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Delivery;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Notifications;

/// <summary>Writes the existing inbox row (<see cref="Notification"/>) from the captured content.</summary>
public sealed class NotificationInAppDeliveryHandler : INotificationChannelDeliveryHandler
{
    private readonly INotificationRepository _notifications;
    private readonly IDateTimeProvider _clock;

    public NotificationInAppDeliveryHandler(INotificationRepository notifications, IDateTimeProvider clock)
    {
        _notifications = notifications;
        _clock = clock;
    }

    public NotificationChannels Channel => NotificationChannels.InApp;

    public async Task DeliverAsync(NotificationDelivery delivery, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        if (delivery.NotificationId is null)
        {
            var notification = new Notification
            {
                Id = Guid.NewGuid(), TenantId = delivery.TenantId, RecipientUserId = delivery.RecipientUserId,
                TemplateCode = delivery.EventKey, Title = delivery.Title, Body = delivery.Body,
                RelatedEntityType = delivery.RelatedEntityType, RelatedEntityId = delivery.RelatedEntityId,
                IsRead = false, CreatedAt = now
            };
            await _notifications.AddAsync(notification, ct);
            delivery.NotificationId = notification.Id;
        }

        delivery.Status = NotificationDeliveryStatus.AvailableInInbox;
        delivery.AttemptCount++;
        delivery.UpdatedAt = now;
        delivery.CompletedAt = now;
    }
}
