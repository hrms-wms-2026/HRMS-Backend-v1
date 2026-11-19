using System.Text.Json;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Delivery;

/// <summary>
/// Delivers one channel of one notification. The outbox processor has already switched to the payload's
/// tenant and saves this delivery's status change together with whatever the channel wrote (e.g. the inbox
/// row), so a retry never replays a channel that already succeeded.
/// </summary>
public sealed class NotificationDeliveryOutboxHandler : IOutboxMessageHandler
{
    private readonly INotificationDeliveryRepository _deliveries;
    private readonly IReadOnlyDictionary<NotificationChannels, INotificationChannelDeliveryHandler> _channels;
    private readonly ILogger<NotificationDeliveryOutboxHandler> _logger;

    public NotificationDeliveryOutboxHandler(
        INotificationDeliveryRepository deliveries,
        IEnumerable<INotificationChannelDeliveryHandler> channels,
        ILogger<NotificationDeliveryOutboxHandler> logger)
    {
        _deliveries = deliveries;
        _channels = channels.ToDictionary(c => c.Channel);
        _logger = logger;
    }

    public string Type => OutboxMessageTypes.NotificationDelivery;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<NotificationDeliveryPayload>(payloadJson)
            ?? throw new InvalidOperationException("notification_delivery payload is empty.");

        var delivery = await _deliveries.GetTrackedAsync(payload.DeliveryId, ct);
        if (delivery is null || delivery.TenantId != payload.TenantId)
        {
            _logger.LogWarning("Notification delivery {DeliveryId} not found for tenant {TenantId}; nothing to send.",
                payload.DeliveryId, payload.TenantId);
            return;
        }
        if (delivery.Status != NotificationDeliveryStatus.Pending)
            return;

        if (!_channels.TryGetValue(delivery.Channel, out var handler))
            throw new InvalidOperationException($"No delivery handler for channel {delivery.Channel}.");

        await handler.DeliverAsync(delivery, ct);
    }
}
