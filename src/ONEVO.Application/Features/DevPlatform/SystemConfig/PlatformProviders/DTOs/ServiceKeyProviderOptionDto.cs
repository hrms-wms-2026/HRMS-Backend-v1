namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.DTOs;

/// <summary>
/// Service-key provider option plus the form the admin UI should render for it.
/// Describes the SHAPE of the inputs only; it never carries any stored value.
/// </summary>
public sealed class ServiceKeyProviderOptionDto
{
    public string ProviderKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool Configured { get; init; }
    public bool IsActive { get; init; }

    /// <summary>"live" (provider is called) or "format-only" (local checks).</summary>
    public string VerificationMode { get; init; } = string.Empty;

    public IReadOnlyList<ServiceKeyFieldDto> Fields { get; init; } = [];
}

public sealed class ServiceKeyFieldDto
{
    public string Name { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public bool Required { get; init; }
    public string? Placeholder { get; init; }
    public string? DefaultValue { get; init; }
    public IReadOnlyList<ServiceKeyFieldOptionDto> Options { get; init; } = [];
}

public sealed class ServiceKeyFieldOptionDto
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}
