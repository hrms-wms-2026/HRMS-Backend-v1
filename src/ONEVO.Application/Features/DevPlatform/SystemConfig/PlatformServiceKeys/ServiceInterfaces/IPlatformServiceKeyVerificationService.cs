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
/// Phase 1 implementation is a safe format-check stub (no live provider calls).
/// SECURITY: the plaintext key is used in memory only and is NEVER logged.
/// </summary>
public interface IPlatformServiceKeyVerificationService
{
    Task<PlatformServiceKeyVerificationResult> VerifyAsync(
        string serviceKey,
        string apiKeyPlaintext,
        CancellationToken ct);
}
