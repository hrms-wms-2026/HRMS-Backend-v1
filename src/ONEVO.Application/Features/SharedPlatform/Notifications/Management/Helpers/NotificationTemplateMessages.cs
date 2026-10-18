using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Helpers;

public static class NotificationTemplateMessages
{
    public const string TemplateNotFound = "Notification template not found.";
    public const string StaleDraft = "This draft was changed by someone else. Reload it to see the latest version before saving.";
    public const string NoDraft = "There is no draft to publish.";
    public const string DraftExists = "Publish or update the current draft before restoring another version.";
    public const int MaxTitleLength = 255;

    /// <summary>Length checks the database enforces; returns null when the content fits.</summary>
    public static string? CheckLengths(NotificationTemplateContent c)
    {
        if (string.IsNullOrWhiteSpace(c.InAppTitle))
            return "In-app title is required.";
        if (c.InAppTitle.Length > MaxTitleLength || (c.EmailSubject?.Length ?? 0) > MaxTitleLength || (c.TrayTitle?.Length ?? 0) > MaxTitleLength)
            return $"Titles and the email subject must be at most {MaxTitleLength} characters.";
        if (c.ActionKey.Length > 50)
            return "Action key is too long.";
        return null;
    }
}
