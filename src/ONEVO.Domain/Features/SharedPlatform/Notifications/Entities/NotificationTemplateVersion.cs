namespace ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

public static class NotificationTemplateVersionStatus
{
    public const string Draft = "draft";
    public const string Published = "published";
}

public enum DraftUpdateOutcome
{
    Updated,
    NotDraft,
    StaleRevision
}

/// <summary>Editable channel copy for one template version. Placeholders are <c>{{variable}}</c>.</summary>
public sealed record NotificationTemplateContent(
    string InAppTitle,
    string InAppBody,
    string? EmailSubject,
    string? EmailHtml,
    string? EmailCss,
    string? EmailText,
    string? TrayTitle,
    string? TrayBody,
    string ActionKey);

/// <summary>
/// One version of a global template. Drafts are edited under an optimistic <see cref="Revision"/>;
/// published versions never change, and restoring an old version copies it into a new draft.
/// </summary>
public class NotificationTemplateVersion
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public int Version { get; set; }
    public string Status { get; set; } = NotificationTemplateVersionStatus.Draft;
    public int Revision { get; set; }
    public string InAppTitle { get; set; } = string.Empty;
    public string InAppBody { get; set; } = string.Empty;
    public string? EmailSubject { get; set; }
    public string? EmailHtml { get; set; }
    public string? EmailCss { get; set; }
    public string? EmailText { get; set; }
    public string? TrayTitle { get; set; }
    public string? TrayBody { get; set; }
    public string ActionKey { get; set; } = "open_related";
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    public static NotificationTemplateVersion CreateDraft(
        Guid templateId, int version, NotificationTemplateContent content, Guid? actorUserId, DateTimeOffset now)
    {
        var draft = new NotificationTemplateVersion
        {
            Id = Guid.NewGuid(), TemplateId = templateId, Version = version,
            Status = NotificationTemplateVersionStatus.Draft, Revision = 1,
            CreatedByUserId = actorUserId, CreatedAt = now
        };
        draft.Apply(content);
        return draft;
    }

    public DraftUpdateOutcome UpdateDraft(NotificationTemplateContent content, int expectedRevision, Guid? actorUserId, DateTimeOffset now)
    {
        if (Status != NotificationTemplateVersionStatus.Draft)
            return DraftUpdateOutcome.NotDraft;
        if (expectedRevision != Revision)
            return DraftUpdateOutcome.StaleRevision;

        Apply(content);
        Revision++;
        UpdatedByUserId = actorUserId;
        UpdatedAt = now;
        return DraftUpdateOutcome.Updated;
    }

    /// <summary>Freezes the draft. Returns false when it is already published.</summary>
    public bool Publish(Guid? actorUserId, DateTimeOffset now)
    {
        if (Status != NotificationTemplateVersionStatus.Draft)
            return false;

        Status = NotificationTemplateVersionStatus.Published;
        Revision++;
        PublishedByUserId = actorUserId;
        PublishedAt = now;
        return true;
    }

    public NotificationTemplateContent ToContent() => new(
        InAppTitle, InAppBody, EmailSubject, EmailHtml, EmailCss, EmailText, TrayTitle, TrayBody, ActionKey);

    private void Apply(NotificationTemplateContent content)
    {
        InAppTitle = content.InAppTitle;
        InAppBody = content.InAppBody;
        EmailSubject = content.EmailSubject;
        EmailHtml = content.EmailHtml;
        EmailCss = content.EmailCss;
        EmailText = content.EmailText;
        TrayTitle = content.TrayTitle;
        TrayBody = content.TrayBody;
        ActionKey = content.ActionKey;
    }
}
