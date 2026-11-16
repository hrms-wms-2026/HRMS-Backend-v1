using ONEVO.Application.Common.Helpers;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Application.Features.DevPlatform.Support.Helpers;

/// <summary>Input limits and state rules shared by the admin and tenant handlers. Limits match the EF configuration.</summary>
public static class SupportTicketRules
{
    public const int SubjectMaxLength = 200;
    public const int DescriptionMaxLength = 10000;
    public const int CategoryMaxLength = 80;
    public const int CommentMaxLength = 10000;
    /// <summary>Cap on the stored HTML of a rich description or comment, whatever its visible length.</summary>
    public const int RawHtmlMaxLength = 50000;
    public const int AnnouncementTitleMaxLength = 200;
    public const int AnnouncementBodyMaxLength = 5000;
    public const int MaxAttachmentsPerTicket = 20;
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public static readonly TimeSpan DownloadUrlLifetime = TimeSpan.FromMinutes(5);

    public static (int Page, int PageSize) NormalizePaging(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));

    /// <summary>Returns an error message, or null when the ticket fields are valid.</summary>
    public static string? ValidateTicket(string? subject, string? description, string? priority, string? category)
    {
        if (string.IsNullOrWhiteSpace(subject)) return "subject is required.";
        if (subject.Trim().Length > SubjectMaxLength) return $"subject must be at most {SubjectMaxLength} characters.";
        if (string.IsNullOrWhiteSpace(description)) return "description is required.";
        var descriptionError = ValidateBody(description, "description", DescriptionMaxLength);
        if (descriptionError is not null) return descriptionError;
        if (RichTextRules.HasActiveContent(subject))
            return RichTextRules.ActiveContentError;
        if (!SupportTicket.Priorities.Contains(priority ?? string.Empty))
            return $"priority must be one of: {string.Join(", ", SupportTicket.Priorities)}.";
        if (category is not null && category.Trim().Length > CategoryMaxLength)
            return $"category must be at most {CategoryMaxLength} characters.";
        return null;
    }

    public static string? ValidateComment(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "body is required.";
        return ValidateBody(body, "body", CommentMaxLength);
    }

    /// <summary>
    /// A description or comment is either plain text (today's rules) or basic rich text from the shared
    /// editor (DEC-035): checked against the shared allowlist, limited by visible text, and capped in raw size.
    /// </summary>
    private static string? ValidateBody(string value, string field, int maxVisible)
    {
        if (!BasicRichHtml.IsRichHtml(value))
        {
            if (value.Trim().Length > maxVisible) return $"{field} must be at most {maxVisible} characters.";
            return RichTextRules.HasActiveContent(value) ? RichTextRules.ActiveContentError : null;
        }

        if (value.Length > RawHtmlMaxLength) return $"{field} formatting is too large (at most {RawHtmlMaxLength} characters of markup).";
        if (BasicRichHtml.VisibleLength(value) > maxVisible) return $"{field} must be at most {maxVisible} characters.";
        var check = BasicRichHtml.Validate(value, field);
        return check.IsValid ? null : check.Error;
    }

    /// <summary>Plain text of a description or comment for emails; never user HTML.</summary>
    public static string EmailExcerpt(string? text) => BasicRichHtml.ToPlainText(text);

    public static string? ValidateAnnouncementText(string title, string body) =>
        RichTextRules.HasActiveContent(title) || RichTextRules.HasActiveContent(body)
            ? RichTextRules.ActiveContentError
            : null;

    public static string? NormalizeCategory(string? category) =>
        string.IsNullOrWhiteSpace(category) ? null : category.Trim();

    public static bool IsFinished(string status) =>
        status is SupportTicket.StatusResolved or SupportTicket.StatusClosed;

    /// <summary>Applies an already-validated edit of the ticket's own fields.</summary>
    public static void ApplyEdit(
        SupportTicket ticket, string subject, string description, string priority, string? category, DateTimeOffset now)
    {
        ticket.Subject = subject.Trim();
        ticket.Description = description.Trim();
        ticket.Priority = priority;
        ticket.Category = NormalizeCategory(category);
        ticket.UpdatedAt = now;
    }

    /// <summary>Moves a ticket to a status, keeping resolved_at in step. A finished ticket waits on nobody.</summary>
    public static void ApplyStatus(SupportTicket ticket, string status, DateTimeOffset now)
    {
        if (IsFinished(status))
        {
            ticket.ResolvedAt ??= now;
            ticket.AwaitingSupport = false;
        }
        else
        {
            ticket.ResolvedAt = null;
        }

        ticket.Status = status;
        ticket.UpdatedAt = now;
    }
}
