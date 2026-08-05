using System.Text.Json.Serialization;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Auth;

public sealed record AdminForgotPasswordRequest
{
    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;
}
