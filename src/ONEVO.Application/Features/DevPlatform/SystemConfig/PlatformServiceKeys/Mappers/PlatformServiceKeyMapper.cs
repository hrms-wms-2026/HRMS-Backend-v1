using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.DTOs.Responses;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Mappers;

/// <summary>
/// Maps PlatformServiceKey entities to safe response DTOs.
/// SECURITY: ApiKeyEncrypted is deliberately never mapped.
/// </summary>
public static class PlatformServiceKeyMapper
{
    public static PlatformServiceKeyDto ToDto(PlatformServiceKey entity) => new()
    {
        Id = entity.Id,
        ServiceKey = entity.ServiceKey,
        DisplayName = entity.DisplayName,
        IsActive = entity.IsActive,
        LastVerifiedAt = entity.LastVerifiedAt,
        UpdatedById = entity.UpdatedById,
        UpdatedAt = entity.UpdatedAt
    };
}
