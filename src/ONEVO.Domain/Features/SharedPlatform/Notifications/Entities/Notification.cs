namespace ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

/// <summary>Per-user in-app notification. RecipientUserId is UserId (not EmployeeId) - notification
/// fields stay UserId per the Phase 2 identity migration's scope boundary. See spec §6.2.</summary>
public class Notification
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RecipientUserId { get; set; }
    public string TemplateCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
