namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.DTOs.Requests;

/// <summary>
/// Create request. ApiKey is plaintext in transit only — encrypted immediately by the
/// command handler via IEncryptionService and never stored or logged raw.
/// </summary>
public sealed class CreatePlatformServiceKeyRequest
{
    public string ServiceKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
}

/// <summary>
/// Metadata-only update. Activation state changes go through the dedicated
/// activate/deactivate endpoints; key material changes go through rotate-key.
/// </summary>
public sealed class UpdatePlatformServiceKeyRequest
{
    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>
/// Rotate request. ApiKey is plaintext in transit only — encrypted immediately and
/// never stored or logged raw.
/// </summary>
public sealed class RotatePlatformServiceKeyRequest
{
    public string ApiKey { get; init; } = string.Empty;
}
