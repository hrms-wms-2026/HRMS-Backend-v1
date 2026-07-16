namespace ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.DTOs.Requests;

public sealed class CreateIntegrationCatalogRequest
{
    public string IntegrationKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string ConnectionScope { get; init; } = string.Empty;
    public string OnevoAppProvider { get; init; } = string.Empty;
    public string? LogoUrl { get; init; }
    public bool? IsActive { get; init; }
}

public sealed class UpdateIntegrationCatalogRequest
{
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string ConnectionScope { get; init; } = string.Empty;
    public string OnevoAppProvider { get; init; } = string.Empty;
    public string? LogoUrl { get; init; }
    public bool IsActive { get; init; }
}
