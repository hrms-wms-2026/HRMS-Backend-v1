using ONEVO.Application.Features.Auth.Permission.DTOs.Responses;
namespace ONEVO.Application.Features.Auth.Permission.DTOs.Responses;

public sealed record PermissionCatalogDto(
    IReadOnlyList<PermissionCatalogItemDto> AssignablePermissions,
    IReadOnlyList<PermissionCatalogItemDto> UniversalPermissions);

public sealed record PermissionCatalogItemDto(
    Guid? Id,
    string Code,
    string Description,
    string Module,
    bool IsAssignable,
    bool IsUniversal);
