using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;

public sealed record SupportTicketDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tenant_id")] Guid? TenantId,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("priority")] string Priority,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("created_by_platform_user_id")] Guid? CreatedByPlatformUserId,
    [property: JsonPropertyName("assigned_to_platform_user_id")] Guid? AssignedToPlatformUserId,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt,
    [property: JsonPropertyName("resolved_at")] DateTimeOffset? ResolvedAt);

public sealed record SupportTicketCommentDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("ticket_id")] Guid TicketId,
    [property: JsonPropertyName("author_platform_user_id")] Guid? AuthorPlatformUserId,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("is_internal")] bool IsInternal,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record SupportTicketDetailDto(
    [property: JsonPropertyName("ticket")] SupportTicketDto Ticket,
    [property: JsonPropertyName("comments")] IReadOnlyList<SupportTicketCommentDto> Comments);

public sealed record SupportTicketListResponseDto(
    [property: JsonPropertyName("items")] IReadOnlyList<SupportTicketDto> Items,
    [property: JsonPropertyName("total")] int TotalCount,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize);
