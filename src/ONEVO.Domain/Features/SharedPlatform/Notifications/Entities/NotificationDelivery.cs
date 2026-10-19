using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

/// <summary>
/// Delivery states. None of them claims the person read the message: provider acceptance is not an
/// opened email, and tray acceptance is not a read toast.
/// </summary>
public static class NotificationDeliveryStatus
{
    public const string Pending = "pending";
    public const string ProviderAccepted = "provider_accepted";
    public const string AvailableInInbox = "available_in_inbox";
    public const string ForwardedToTray = "forwarded_to_tray";
    public const string ClientAccepted = "client_accepted";
    public const string Skipped = "skipped";
    public const string Expired = "expired";
    public const string Failed = "failed";
}

/// <summary>
/// One channel's delivery of one event to one recipient. Unique per (tenant, event, recipient, channel),
/// so a retried or duplicated event never produces a second internal send. The rendered content is
/// captured at enqueue time: later template edits never change a queued message.
/// </summary>
public class NotificationDelivery : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public Guid RecipientUserId { get; set; }
    public NotificationChannels Channel { get; set; }
    public Guid? TemplateVersionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? EmailSubject { get; set; }
    public string? EmailHtml { get; set; }
    public string? EmailText { get; set; }
    public string ActionKey { get; set; } = "open_related";
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public string Status { get; set; } = NotificationDeliveryStatus.Pending;
    /// <summary>Why the channel was skipped or failed (no email address, no tray device...). Never message content.</summary>
    public string? StatusReason { get; set; }
    public int AttemptCount { get; set; }
    /// <summary>The in-app inbox row this delivery created.</summary>
    public Guid? NotificationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    /// <summary>Tray only: after this, an undelivered alert is marked Expired instead of shown late.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public bool IsTerminal => Status is not (NotificationDeliveryStatus.Pending or NotificationDeliveryStatus.ForwardedToTray);
}
