namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.DTOs;

/// <summary>
/// Minimal provider selection option for System Config screen dropdowns/cards.
/// Contains no credential material, provider family, or internal identifiers.
/// </summary>
public sealed class ProviderOptionDto
{
    public string ProviderKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool Configured { get; init; }
    public bool IsActive { get; init; }
}
