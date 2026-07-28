using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Admin.Legal;

/// <summary>content_hash is deliberately absent - the backend always recomputes it, never accepts it.</summary>
public sealed record PublishLegalDocumentVersionRequest(
    [property: JsonPropertyName("publish_reason")] string? PublishReason);
