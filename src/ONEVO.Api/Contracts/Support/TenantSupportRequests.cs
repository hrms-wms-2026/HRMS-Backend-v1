using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Support;

/// <summary>No tenant_id: the ticket always belongs to the caller's tenant.</summary>
public sealed record CreateTenantSupportTicketRequest(
    [property: JsonPropertyName("subject")] string? Subject,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("priority")] string? Priority,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("correlation_ids")] IReadOnlyList<string?>? CorrelationIds = null);

/// <summary>No is_internal: tenant comments are always visible to both sides.</summary>
public sealed record AddTenantSupportTicketCommentRequest(
    [property: JsonPropertyName("body")] string? Body);
