using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Auth.Legal.Services;

public enum LegalAcceptanceStatus
{
    Complete,
    Pending,
    NotConfigured
}

public record PendingLegalDocumentDto(
    [property: JsonPropertyName("document_type")] string DocumentType,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("effective_at")] DateTimeOffset? EffectiveAt,
    [property: JsonPropertyName("content_url")] string? ContentUrl
);

public record LegalAcceptanceCheckResult(
    LegalAcceptanceStatus Status,
    bool IsComplete,
    IReadOnlyList<PendingLegalDocumentDto> PendingDocuments,
    string? ErrorCode = null
);

public interface ILegalAcceptanceChecker
{
    Task<LegalAcceptanceCheckResult> CheckAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
