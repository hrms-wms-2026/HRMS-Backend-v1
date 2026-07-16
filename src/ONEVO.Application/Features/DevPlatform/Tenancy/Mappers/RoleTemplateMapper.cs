using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Mappers;

internal static class RoleTemplateMapper
{
    internal static RoleTemplateDto ToDto(RoleTemplate t) =>
        new(
            t.Id,
            t.Name,
            string.IsNullOrWhiteSpace(t.Description) ? null : t.Description,
            RoleTemplateJson.DeserializeStringList(t.ModuleKeysJson),
            RoleTemplateJson.DeserializeStringList(t.PermissionCodesJson),
            t.IsSystem,
            t.Version,
            t.IsActive,
            t.CreatedAt,
            t.UpdatedAt);
}
