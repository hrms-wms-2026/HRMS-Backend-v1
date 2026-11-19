namespace ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

/// <summary>Global (not tenant-scoped) template - product copy, not tenant configuration. See
/// docs/superpowers/specs/next/2026-08-16-work-management-task-foundation-design.md §6.1.
/// Wording lives in immutable <see cref="NotificationTemplateVersion"/> rows; the in-app columns below
/// mirror the active published version so the legacy dispatcher keeps sending current copy.</summary>
public class NotificationTemplate
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    /// <summary>The catalog event this template renders. Product defaults use Code == EventKey.</summary>
    public string EventKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Locale { get; set; } = "en";
    public Guid? ActiveVersionId { get; set; }
    public string InAppTitleTemplate { get; set; } = string.Empty;
    public string InAppBodyTemplate { get; set; } = string.Empty;
    public string? MailSubjectTemplate { get; set; }
    public string? MailBodyTemplate { get; set; }
    public bool InAppEnabled { get; set; } = true;
    public bool MailEnabled { get; set; } = false;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Makes a published version the live wording for new sends.</summary>
    public void Activate(NotificationTemplateVersion version, DateTimeOffset now)
    {
        if (version.TemplateId != Id)
            throw new InvalidOperationException("The version belongs to a different template.");
        if (version.Status != NotificationTemplateVersionStatus.Published)
            throw new InvalidOperationException("Only a published version can be activated.");

        ActiveVersionId = version.Id;
        InAppTitleTemplate = version.InAppTitle;
        InAppBodyTemplate = version.InAppBody;
        MailSubjectTemplate = version.EmailSubject;
        MailBodyTemplate = version.EmailText;
        UpdatedAt = now;
    }
}
