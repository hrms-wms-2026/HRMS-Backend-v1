namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.DTOs;

/// <summary>
/// Metadata and masked configuration state for a System Config provider card.
/// This response contains no credential or encrypted payload fields.
/// </summary>
public sealed class PlatformProviderCardDto
{
    public Guid Id { get; init; }
    public string ProviderKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ProviderFamily { get; init; } = string.Empty;
    public bool Configured { get; init; }
    public bool ConfigurationActive { get; init; }
    public DateTimeOffset? LastVerifiedAt { get; init; }
}
