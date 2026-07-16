using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Infrastructure.Services.DevPlatform.PlatformAccess;

/// <summary>
/// Explicit database-backed platform permission resolution:
/// load user -> check status -> load user roles -> load active roles ->
/// load role permission grants -> union permission codes.
/// Written as plain loops on purpose so the business rule stays visible.
/// </summary>
public sealed class PlatformPermissionResolver : IPlatformPermissionResolver
{
    private readonly IPlatformUserRepository _users;
    private readonly IPlatformAccessReadRepository _access;

    public PlatformPermissionResolver(IPlatformUserRepository users, IPlatformAccessReadRepository access)
    {
        _users = users;
        _access = access;
    }

    public async Task<PlatformAccessProfile?> ResolveActiveUserAsync(Guid platformUserId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(platformUserId, ct);
        if (user is null)
            return null;

        if (user.Status != PlatformUser.StatusActive)
            return null;

        var userRoles = await _access.GetUserRolesAsync(user.Id, ct);

        var roleIds = new List<Guid>();
        foreach (var userRole in userRoles)
        {
            roleIds.Add(userRole.RoleId);
        }

        var roleNames = new List<string>();
        var activeRoleIds = new List<Guid>();
        if (roleIds.Count > 0)
        {
            var roles = await _access.GetRolesByIdsAsync(roleIds, ct);
            foreach (var role in roles)
            {
                if (!role.IsActive)
                    continue;

                activeRoleIds.Add(role.Id);
                roleNames.Add(role.Name);
            }
        }

        var permissionCodes = new HashSet<string>(StringComparer.Ordinal);
        if (activeRoleIds.Count > 0)
        {
            var grants = await _access.GetRolePermissionsAsync(activeRoleIds, ct);
            foreach (var grant in grants)
            {
                permissionCodes.Add(grant.PermissionCode);
            }
        }

        return new PlatformAccessProfile
        {
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Status = user.Status,
            RoleNames = roleNames,
            PermissionCodes = permissionCodes
        };
    }
}
