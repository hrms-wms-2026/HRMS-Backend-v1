namespace ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

/// <summary>Global (not tenant-scoped) template - product copy, not tenant configuration. See
/// docs/superpowers/specs/next/2026-08-16-work-management-task-foundation-design.md §6.1.</summary>
public class NotificationTemplate
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string InAppTitleTemplate { get; set; } = string.Empty;
    public string InAppBodyTemplate { get; set; } = string.Empty;
    public string? MailSubjectTemplate { get; set; }
    public string? MailBodyTemplate { get; set; }
    public bool InAppEnabled { get; set; } = true;
    public bool MailEnabled { get; set; } = false;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
