using System.Text.Json.Serialization;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Auth;

public sealed record AdminResetPasswordRequest
{
    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;

    [JsonPropertyName("newPassword")]
    public string NewPassword { get; init; } = string.Empty;
}
