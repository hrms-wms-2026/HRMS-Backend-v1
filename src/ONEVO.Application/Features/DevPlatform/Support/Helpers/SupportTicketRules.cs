using System.Text.RegularExpressions;
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

    /// <summary>A ticket carries at most this many correlation ids (the tenant app keeps the last 5 failed requests).</summary>
    public const int MaxCorrelationIds = 5;
    public const int CorrelationIdMaxLength = 100;

    /// <summary>support_tickets.correlation_ids is nvarchar(1000); 5 ids of 100 chars plus separators fit.</summary>
    public const int CorrelationIdsColumnMaxLength = 1000;

    public const string CorrelationIdsCountError = "correlation_ids must contain at most 5 items.";
    public const string CorrelationIdFormatError =
        "each correlation id must be 1 to 100 characters of letters, digits and '-'.";

    private static readonly Regex CorrelationIdPattern = new(@"^[A-Za-z0-9-]+\z", RegexOptions.CultureInvariant);

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

    /// <summary>Returns an error message, or null when the correlation ids are valid. A null or empty list is valid.</summary>
    public static string? ValidateCorrelationIds(IReadOnlyList<string?>? correlationIds)
    {
        if (correlationIds is null || correlationIds.Count == 0) return null;
        if (correlationIds.Count > MaxCorrelationIds) return CorrelationIdsCountError;
        return correlationIds.All(IsValidCorrelationId) ? null : CorrelationIdFormatError;
    }

    public static bool IsValidCorrelationId(string? id) =>
        id is not null && id.Length <= CorrelationIdMaxLength && CorrelationIdPattern.IsMatch(id);

    /// <summary>Stores already-validated ids comma-separated, duplicates removed, order kept. Null when none.</summary>
    public static string? JoinCorrelationIds(IReadOnlyList<string?>? correlationIds) =>
        correlationIds is null || correlationIds.Count == 0
            ? null
            : string.Join(',', correlationIds.Distinct(StringComparer.Ordinal));

    public static IReadOnlyList<string> SplitCorrelationIds(string? stored) =>
        string.IsNullOrEmpty(stored)
            ? []
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
