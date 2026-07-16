using System.Text.Json.Serialization;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Auth;

public sealed record AdminGoogleCallbackRequest(
    [property: JsonPropertyName("google_id_token")] string GoogleIdToken);
