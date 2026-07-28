using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Auth;

public record LegalAcceptanceItemRequest(
    [property: JsonPropertyName("document_type")] string DocumentType,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("content_hash")] string? ContentHash = null);

public record AcceptPendingLegalDocumentsRequest(
    [property: JsonPropertyName("acceptances")] IReadOnlyList<LegalAcceptanceItemRequest> Acceptances);
