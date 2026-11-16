using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Admin.Support;

public sealed record CreateSupportTicketRequest(
    [property: JsonPropertyName("tenant_id")] Guid? TenantId,
    [property: JsonPropertyName("subject")] string? Subject,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("priority")] string? Priority,
    [property: JsonPropertyName("category")] string? Category);

public sealed record UpdateSupportTicketStatusRequest(
    [property: JsonPropertyName("status")] string? Status);

/// <summary>null clears the assignee.</summary>
public sealed record AssignSupportTicketRequest(
    [property: JsonPropertyName("assigned_to_platform_user_id")] Guid? AssignedToPlatformUserId);

public sealed record AddSupportTicketCommentRequest(
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("is_internal")] bool IsInternal);

/// <summary>Edits the ticket's own fields. Status and assignee have their own endpoints.</summary>
public sealed record UpdateSupportTicketRequest(
    [property: JsonPropertyName("subject")] string? Subject,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("priority")] string? Priority,
    [property: JsonPropertyName("category")] string? Category);

/// <summary>Used for create and for edit (a full replacement). starts_at / ends_at are optional.</summary>
public sealed record PlatformAnnouncementRequest(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("audience")] string? Audience,
    [property: JsonPropertyName("starts_at")] DateTimeOffset? StartsAt,
    [property: JsonPropertyName("ends_at")] DateTimeOffset? EndsAt);
