using System.Text.Json;
using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;

/// <summary>Lightweight list-row shape - deliberately excludes content_json/content_html/content_text.</summary>
public sealed record LegalDocumentVersionSummaryDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("document_type")] string DocumentType,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("is_required")] bool IsRequired,
    [property: JsonPropertyName("block_scope")] string BlockScope,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("published_by_id")] Guid? PublishedById,
    [property: JsonPropertyName("content_hash")] string ContentHash,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>Full detail shape, including content. Used only by the admin single-version GET.</summary>
public sealed record LegalDocumentVersionDetailDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("document_type")] string DocumentType,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("is_required")] bool IsRequired,
    [property: JsonPropertyName("block_scope")] string BlockScope,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("published_by_id")] Guid? PublishedById,
    [property: JsonPropertyName("content_json")] JsonElement ContentJson,
    [property: JsonPropertyName("content_html")] string ContentHtml,
    [property: JsonPropertyName("content_text")] string ContentText,
    [property: JsonPropertyName("content_hash")] string ContentHash,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>Public/tenant read shape for the two content-read endpoints. Never includes tenant/user IDs.</summary>
public sealed record PublishedLegalDocumentDto(
    [property: JsonPropertyName("document_type")] string DocumentType,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("content_html")] string ContentHtml,
    [property: JsonPropertyName("content_text")] string ContentText,
    [property: JsonPropertyName("effective_at")] DateTimeOffset? EffectiveAt,
    [property: JsonPropertyName("content_hash")] string ContentHash);
