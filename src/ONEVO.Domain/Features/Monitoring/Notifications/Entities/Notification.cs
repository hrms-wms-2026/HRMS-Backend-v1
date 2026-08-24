using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Notifications.Entities;

public enum NotificationType { BreakReminder, LongIdleAlert, LowActivityAlert, FocusNudge }

public class Notification : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    /// <summary>JSON blob of trigger-specific context, e.g. {"idleMinutes":35}. Never PII beyond EmployeeId.</summary>
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeliveredToTrayAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
