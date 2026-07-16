using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;

public interface IPlatformAccessManagementService
{
    /// <summary>
    /// Validates if an updated role assignment maintains at least one active user with both AccountsManage and RolesManage.
    /// Throws InvalidOperationException if the action would result in a lockout.
    /// </summary>
    Task ValidateUserRoleLockoutPreventionAsync(Guid userId, IReadOnlyList<Guid> newRoleIds, CancellationToken ct = default);

    /// <summary>
    /// Validates if an updated role permission set maintains at least one active user with both AccountsManage and RolesManage.
    /// Throws InvalidOperationException if the action would result in a lockout.
    /// </summary>
    Task ValidateRolePermissionLockoutPreventionAsync(Guid roleId, IReadOnlyList<string> newPermissions, CancellationToken ct = default);
}
