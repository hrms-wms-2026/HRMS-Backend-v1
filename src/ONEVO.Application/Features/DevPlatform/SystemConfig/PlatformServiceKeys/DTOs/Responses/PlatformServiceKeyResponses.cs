namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.DTOs.Responses;

/// <summary>
/// Platform service key list/detail response.
/// SECURITY: neither the plaintext API key nor api_key_encrypted is EVER included.
/// </summary>
public sealed class PlatformServiceKeyDto
{
    public Guid Id { get; init; }
    public string ServiceKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTimeOffset? LastVerifiedAt { get; init; }
    public Guid UpdatedById { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Result of verifying a saved platform service key. No secret material is included.
/// </summary>
public sealed class ServiceKeyVerificationResultDto
{
    public bool Success { get; init; }
    public DateTimeOffset CheckedAt { get; init; }
    public string Message { get; init; } = string.Empty;
}
