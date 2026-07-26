using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Auth;

public record LegalAcceptanceItemRequest(
    [property: JsonPropertyName("document_type")] string DocumentType,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("decision")] string Decision);

public record AcceptPendingLegalDocumentsRequest(
    [property: JsonPropertyName("csrf_token")] string CsrfToken,
    [property: JsonPropertyName("acceptances")] IReadOnlyList<LegalAcceptanceItemRequest> Acceptances);
