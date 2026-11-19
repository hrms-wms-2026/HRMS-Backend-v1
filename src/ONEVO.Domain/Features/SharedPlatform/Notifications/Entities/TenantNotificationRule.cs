using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

/// <summary>
/// A tenant's override of how one catalog event is delivered. No row means the catalog defaults apply;
/// resetting deletes the row.
/// </summary>
public class TenantNotificationRule : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string EventKey { get; set; } = string.Empty;
    /// <summary>Selected published template for the event; null = the product default template.</summary>
    public Guid? TemplateId { get; set; }
    public NotificationChannels EnabledChannels { get; set; }
    public string RecipientRule { get; set; } = "default";
    /// <summary>Optimistic concurrency token; a stale save returns 409.</summary>
    public int Revision { get; set; } = 1;
    public Guid? UpdatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
