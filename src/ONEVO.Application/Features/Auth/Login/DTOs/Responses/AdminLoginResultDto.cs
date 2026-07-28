using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Auth.Login.DTOs.Responses;

/// <summary>
/// Internal handler-to-controller result for admin auth.
/// </summary>
public record AdminLoginResultDto(
    string CsrfToken,
    string CsrfTokenHash,
    DateTimeOffset? ExpiresAt,
    Guid PlatformUserId,
    string Email,
    string PlatformRole,
    bool RequiresMfa = false,
    string? MfaSessionToken = null)
{
    public AdminSessionResponseDto ToSessionResponse() =>
        new(
            PlatformUserId: RequiresMfa ? Guid.Empty : PlatformUserId,
            Email: RequiresMfa ? string.Empty : Email,
            PlatformRole: RequiresMfa ? string.Empty : PlatformRole,
            ExpiresAt: RequiresMfa ? DateTimeOffset.MinValue : (ExpiresAt ?? DateTimeOffset.MinValue),
            MfaRequired: RequiresMfa);
}

public sealed record AdminSessionResponseDto(
    [property: JsonPropertyName("platform_user_id")] Guid PlatformUserId,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("platform_role")] string PlatformRole,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("mfa_required")] bool MfaRequired = false);
