using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;
namespace ONEVO.Application.Features.Auth.Roles.DTOs.Responses;

public record RoleDetailDto(
    Guid Id,
    string Name,
    string Description,
    bool IsSystem,
    IReadOnlyList<RolePermissionDto> Permissions,
    IReadOnlyList<RolePermissionDto> UniversalPermissions,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public record RolePermissionDto(Guid Id, string Code, string Description, string Module);
