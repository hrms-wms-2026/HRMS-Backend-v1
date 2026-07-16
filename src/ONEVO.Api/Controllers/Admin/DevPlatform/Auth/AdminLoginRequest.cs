using System.Text.Json.Serialization;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Auth;

public sealed record AdminLoginRequest
{
    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; init; } = string.Empty;
}
