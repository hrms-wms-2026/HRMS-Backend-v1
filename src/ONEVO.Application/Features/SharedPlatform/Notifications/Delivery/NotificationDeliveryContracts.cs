using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Delivery;

/// <summary>
/// One occurrence of a catalog event for one recipient. <see cref="EventId"/> must be stable for the
/// occurrence (generated once inside the producer's transaction), so a duplicate or retried enqueue
/// collapses onto the same delivery records.
/// </summary>
public sealed record NotificationDispatchRequest(
    Guid EventId,
    string EventKey,
    Guid TenantId,
    Guid RecipientUserId,
    IReadOnlyDictionary<string, string> Variables,
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null);

/// <summary>
/// Resolves the tenant's routing and the active template version, renders the content once and reserves
/// one delivery per enabled channel plus its outbox work - all in the caller's unit of work. Never commits:
/// if the business transaction rolls back, no notification work exists.
/// </summary>
public interface INotificationOrchestrator
{
    Task EnqueueAsync(NotificationDispatchRequest request, CancellationToken ct = default);
}

/// <summary>Outbox payload for one channel delivery. Content is reloaded from the delivery row, never carried here.</summary>
public sealed record NotificationDeliveryPayload(Guid DeliveryId, Guid TenantId);

/// <summary>Delivers one pending delivery on its channel and updates only that delivery's status.</summary>
public interface INotificationChannelDeliveryHandler
{
    NotificationChannels Channel { get; }

    Task DeliverAsync(NotificationDelivery delivery, CancellationToken ct);
}

/// <summary>Thrown to hand a transient channel failure back to the outbox retry policy. The delivery's
/// attempt count and reason are already updated and are saved with the failed attempt.</summary>
public sealed class NotificationDeliveryRetryException : Exception
{
    public NotificationDeliveryRetryException(string message) : base(message)
    {
    }
}
