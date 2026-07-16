using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;

namespace ONEVO.Infrastructure.Services.DevPlatform.PlatformAccess;

public sealed class PlatformAccessManagementService : IPlatformAccessManagementService
{
    private readonly IPlatformUserRepository _userRepository;
    private readonly IPlatformRoleRepository _roleRepository;
    private readonly IPlatformAccessReadRepository _readRepository;

    public PlatformAccessManagementService(
        IPlatformUserRepository userRepository,
        IPlatformRoleRepository roleRepository,
        IPlatformAccessReadRepository readRepository)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _readRepository = readRepository;
    }

    public async Task ValidateUserRoleLockoutPreventionAsync(Guid userId, IReadOnlyList<Guid> newRoleIds, CancellationToken ct = default)
    {
        var allUsers = await _userRepository.ListUsersAsync(ct);
        var allRoles = await _roleRepository.ListRolesAsync(ct);
        var allRolePermissions = await _readRepository.GetRolePermissionsAsync(allRoles.Select(r => r.Id).ToList(), ct);

        int activeAdmins = 0;

        foreach (var user in allUsers.Where(u => u.Status == ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities.PlatformUser.StatusActive))
        {
            var userRoles = user.Id == userId 
                ? newRoleIds 
                : (await _readRepository.GetUserRolesAsync(user.Id, ct)).Select(ur => ur.RoleId).ToList();

            var userPermissions = allRolePermissions
                .Where(rp => userRoles.Contains(rp.RoleId))
                .Select(rp => rp.PermissionCode)
                .ToHashSet();

            if (userPermissions.Contains(PlatformPermissionCatalog.AccountsManage) &&
                userPermissions.Contains(PlatformPermissionCatalog.RolesManage))
            {
                activeAdmins++;
            }
        }

        if (activeAdmins == 0)
        {
            throw new InvalidOperationException("This action is not allowed because it would remove the last active platform user with both accounts and roles management permissions.");
        }
    }

    public async Task ValidateRolePermissionLockoutPreventionAsync(Guid roleId, IReadOnlyList<string> newPermissions, CancellationToken ct = default)
    {
        var allUsers = await _userRepository.ListUsersAsync(ct);
        var allRoles = await _roleRepository.ListRolesAsync(ct);
        var allRolePermissions = await _readRepository.GetRolePermissionsAsync(allRoles.Select(r => r.Id).ToList(), ct);

        int activeAdmins = 0;

        foreach (var user in allUsers.Where(u => u.Status == ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities.PlatformUser.StatusActive))
        {
            var userRoles = (await _readRepository.GetUserRolesAsync(user.Id, ct)).Select(ur => ur.RoleId).ToList();

            var userPermissions = allRolePermissions
                .Where(rp => rp.RoleId != roleId && userRoles.Contains(rp.RoleId))
                .Select(rp => rp.PermissionCode)
                .ToHashSet();

            if (userRoles.Contains(roleId))
            {
                foreach (var perm in newPermissions)
                {
                    userPermissions.Add(perm);
                }
            }

            if (userPermissions.Contains(PlatformPermissionCatalog.AccountsManage) &&
                userPermissions.Contains(PlatformPermissionCatalog.RolesManage))
            {
                activeAdmins++;
            }
        }

        if (activeAdmins == 0)
        {
            throw new InvalidOperationException("This action is not allowed because it would remove the last active platform user with both accounts and roles management permissions.");
        }
    }
}
