using System.Text.Json.Serialization;

namespace ONEVO.Api.Controllers.Admin.DevPlatform.Auth;

public sealed record VerifyAdminMfaRequest
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;
}
