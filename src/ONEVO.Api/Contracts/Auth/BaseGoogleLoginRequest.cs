using System.Text.Json.Serialization;

namespace ONEVO.Api.Contracts.Auth;

public record BaseGoogleLoginRequest(
    [property: JsonPropertyName("google_id_token")] string GoogleIdToken);
