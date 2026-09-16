namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Definitions;

public static class ServiceKeyFieldKinds
{
    public const string Text = "text";
    public const string Secret = "secret";
    public const string Select = "select";
    public const string Url = "url";
}

public sealed record ServiceKeyFieldOption(string Value, string Label);

/// <summary>
/// One input an admin must supply for a service key. The backend owns this shape; the
/// admin UI renders whatever fields it is told about, so a new integration never needs
/// provider-specific frontend code.
/// </summary>
public sealed record ServiceKeyFieldDefinition(
    string Name,
    string Label,
    string Kind,
    bool Required = true,
    string? Placeholder = null,
    string? DefaultValue = null,
    IReadOnlyList<ServiceKeyFieldOption>? Options = null);
