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
        if (description.Trim().Length > DescriptionMaxLength)
            return $"description must be at most {DescriptionMaxLength} characters.";
        if (RichTextRules.HasActiveContent(subject) || RichTextRules.HasActiveContent(description))
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
        if (body.Trim().Length > CommentMaxLength) return $"body must be at most {CommentMaxLength} characters.";
        if (RichTextRules.HasActiveContent(body)) return RichTextRules.ActiveContentError;
        return null;
    }

    public static string? ValidateAnnouncementText(string title, string body) =>
        RichTextRules.HasActiveContent(title) || RichTextRules.HasActiveContent(body)
            ? RichTextRules.ActiveContentError
            : null;

    public static string? NormalizeCategory(string? category) =>
        string.IsNullOrWhiteSpace(category) ? null : category.Trim();

    public static bool IsFinished(string status) =>
        status is SupportTicket.StatusResolved or SupportTicket.StatusClosed;

    /// <summary>Moves a ticket to a status, keeping resolved_at in step.</summary>
    public static void ApplyStatus(SupportTicket ticket, string status, DateTimeOffset now)
    {
        if (IsFinished(status))
        {
            ticket.ResolvedAt ??= now;
        }
        else
        {
            ticket.ResolvedAt = null;
        }

        ticket.Status = status;
        ticket.UpdatedAt = now;
    }
}
