using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.DevPlatform.Support.DTOs;

public sealed record SupportPageDto<T>(
    [property: JsonPropertyName("items")] IReadOnlyList<T> Items,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize);

// ---- Platform admin shapes -------------------------------------------------------------------

public sealed record SupportTicketDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("tenant_name")] string TenantName,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("priority")] string Priority,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("created_by_platform_user_id")] Guid? CreatedByPlatformUserId,
    [property: JsonPropertyName("created_by_user_id")] Guid? CreatedByUserId,
    [property: JsonPropertyName("created_by_name")] string CreatedByName,
    [property: JsonPropertyName("assigned_to_platform_user_id")] Guid? AssignedToPlatformUserId,
    [property: JsonPropertyName("assigned_to_name")] string? AssignedToName,
    [property: JsonPropertyName("awaiting_support")] bool AwaitingSupport,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt,
    [property: JsonPropertyName("resolved_at")] DateTimeOffset? ResolvedAt);

/// <summary>Counts behind the admin console's support bell.</summary>
public sealed record SupportSummaryDto(
    [property: JsonPropertyName("awaiting_support")] int AwaitingSupport,
    [property: JsonPropertyName("awaiting_support_assigned_to_me")] int AwaitingSupportAssignedToMe,
    [property: JsonPropertyName("open_unassigned")] int OpenUnassigned);

public sealed record SupportTicketCommentDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("ticket_id")] Guid TicketId,
    [property: JsonPropertyName("author_platform_user_id")] Guid? AuthorPlatformUserId,
    [property: JsonPropertyName("author_user_id")] Guid? AuthorUserId,
    [property: JsonPropertyName("author_name")] string AuthorName,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("is_internal")] bool IsInternal,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record SupportTicketAttachmentDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("ticket_id")] Guid TicketId,
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("content_type")] string ContentType,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("is_internal")] bool IsInternal,
    [property: JsonPropertyName("uploaded_by_platform_user_id")] Guid? UploadedByPlatformUserId,
    [property: JsonPropertyName("uploaded_by_user_id")] Guid? UploadedByUserId,
    [property: JsonPropertyName("uploaded_by_name")] string UploadedByName,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record SupportTicketDetailDto(
    [property: JsonPropertyName("ticket")] SupportTicketDto Ticket,
    [property: JsonPropertyName("comments")] IReadOnlyList<SupportTicketCommentDto> Comments,
    [property: JsonPropertyName("attachments")] IReadOnlyList<SupportTicketAttachmentDto> Attachments);

public sealed record SupportAssigneeDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("email")] string Email);

public sealed record SupportAttachmentDownloadDto(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);

public sealed record PlatformAnnouncementDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("audience")] string Audience,
    [property: JsonPropertyName("is_published")] bool IsPublished,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("starts_at")] DateTimeOffset? StartsAt,
    [property: JsonPropertyName("ends_at")] DateTimeOffset? EndsAt,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt);

/// <summary>Banner shape shared by the admin console and the tenant app.</summary>
public sealed record ActiveAnnouncementDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt);

// ---- Tenant shapes: never carry internal notes or platform user ids ---------------------------

public sealed record TenantSupportTicketDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("priority")] string Priority,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("created_by_name")] string CreatedByName,
    [property: JsonPropertyName("raised_by_support")] bool RaisedBySupport,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt,
    [property: JsonPropertyName("resolved_at")] DateTimeOffset? ResolvedAt);

public sealed record TenantSupportTicketCommentDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("ticket_id")] Guid TicketId,
    [property: JsonPropertyName("author_name")] string AuthorName,
    [property: JsonPropertyName("is_from_support")] bool IsFromSupport,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record TenantSupportTicketAttachmentDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("ticket_id")] Guid TicketId,
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("content_type")] string ContentType,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("uploaded_by_name")] string UploadedByName,
    [property: JsonPropertyName("is_from_support")] bool IsFromSupport,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record TenantSupportTicketDetailDto(
    [property: JsonPropertyName("ticket")] TenantSupportTicketDto Ticket,
    [property: JsonPropertyName("comments")] IReadOnlyList<TenantSupportTicketCommentDto> Comments,
    [property: JsonPropertyName("attachments")] IReadOnlyList<TenantSupportTicketAttachmentDto> Attachments);
