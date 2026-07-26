using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Auth;

public record AcceptInvitationGoogleRequest(
    [property: JsonPropertyName("google_id_token")] string GoogleIdToken,
    [property: JsonPropertyName("acceptances")] IReadOnlyList<LegalAcceptanceItemRequest> Acceptances);
