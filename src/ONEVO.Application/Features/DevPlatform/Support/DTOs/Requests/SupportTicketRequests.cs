using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.DevPlatform.Support.DTOs.Requests;

public sealed record CreateSupportTicketRequest(
    [property: JsonPropertyName("tenant_id")] Guid? TenantId,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("priority")] string? Priority,
    [property: JsonPropertyName("category")] string? Category);

public sealed record UpdateSupportTicketStatusRequest(
    [property: JsonPropertyName("status")] string Status);

public sealed record AddSupportTicketCommentRequest(
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("is_internal")] bool IsInternal);
