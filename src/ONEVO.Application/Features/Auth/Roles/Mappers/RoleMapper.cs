using ONEVO.Application.Features.Auth.Permission.DTOs.Responses;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;
using ONEVO.Domain.Features.Auth.Entities;
using PermissionEntity = ONEVO.Domain.Features.Auth.Entities.Permission;

namespace ONEVO.Application.Features.Auth.Roles.Mappers;

internal static class RoleMapper
{
    public static RoleSummaryDto ToSummaryDto(Role role, int permissionCount) =>
        new(
            Id: role.Id,
            Name: role.Name,
            Description: role.Description,
            IsSystem: role.IsSystem,
            PermissionCount: permissionCount,
            CreatedAt: role.CreatedAt,
            UpdatedAt: role.UpdatedAt,
            SourceTemplateId: role.SourceTemplateId);

    public static RoleDetailDto ToDetailDto(
        Role role,
        IReadOnlyList<PermissionEntity> permissions,
        IReadOnlyList<RolePermissionDto> universalPermissions) =>
        new(
            Id: role.Id,
            Name: role.Name,
            Description: role.Description,
            IsSystem: role.IsSystem,
            Permissions: permissions
                .OrderBy(p => p.Module, StringComparer.Ordinal)
                .ThenBy(p => p.Code, StringComparer.Ordinal)
                .Select(ToPermissionDto)
                .ToList(),
            UniversalPermissions: universalPermissions,
            CreatedAt: role.CreatedAt,
            UpdatedAt: role.UpdatedAt);

    public static RolePermissionDto ToPermissionDto(PermissionEntity p) =>
        new(p.Id, p.Code, p.Description, p.Module);

    public static IReadOnlyList<RolePermissionDto> MapUniversalPermissions(PermissionCatalogDto catalog) =>
        catalog.UniversalPermissions
            .Select(u => new RolePermissionDto(u.Id ?? Guid.Empty, u.Code, u.Description, u.Module))
            .ToList();
}
