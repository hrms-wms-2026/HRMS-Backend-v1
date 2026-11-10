namespace ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.DTOs.Responses;

public sealed record IntegrationCatalogDto(
    string IntegrationKey, string DisplayName, string? Description, string ConnectionScope,
    string CrownusAppProvider, string? LogoUrl, bool IsActive, DateTimeOffset CreatedAt,
    IReadOnlyList<string> LinkedModuleKeys);
