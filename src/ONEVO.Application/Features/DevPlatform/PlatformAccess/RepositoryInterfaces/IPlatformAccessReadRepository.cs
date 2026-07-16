using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;

/// <summary>
/// Read access for platform RBAC resolution:
/// platform_user_roles -> platform_roles -> platform_role_permissions.
/// </summary>
public interface IPlatformAccessReadRepository
{
    Task<List<PlatformUserRole>> GetUserRolesAsync(Guid userId, CancellationToken ct = default);
    Task<List<PlatformRole>> GetRolesByIdsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken ct = default);
    Task<List<PlatformRolePermission>> GetRolePermissionsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken ct = default);
}
