namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;

/// <summary>
/// Outcome of a platform service key verification attempt. Contains no secret material.
/// </summary>
public sealed class PlatformServiceKeyVerificationResult
{
    public bool Success { get; init; }
    public DateTimeOffset CheckedAt { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Verifies a platform service API key for a given service slug.
/// Transactional email providers use lightweight live checks; other supported
/// services may remain local format-only until provider clients are wired.
/// SECURITY: the plaintext key is used in memory only and is NEVER logged.
/// </summary>
public interface IPlatformServiceKeyVerificationService
{
    Task<PlatformServiceKeyVerificationResult> VerifyAsync(
        string serviceKey,
        string apiKeyPlaintext,
        CancellationToken ct);
}
