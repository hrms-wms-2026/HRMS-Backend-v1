using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.DTOs.Responses;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Mappers;

public static class IntegrationCatalogMapper
{
    public static IntegrationCatalogDto ToDto(IntegrationCatalogEntry entry, IReadOnlyList<string> moduleKeys) =>
        new(entry.IntegrationKey, entry.DisplayName, entry.Description, entry.ConnectionScope,
            entry.OnevoAppProvider, entry.LogoUrl, entry.IsActive, entry.CreatedAt, moduleKeys);
}
