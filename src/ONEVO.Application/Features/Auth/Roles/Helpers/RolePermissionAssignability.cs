using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using AuthPermission = ONEVO.Domain.Features.Auth.Entities.Permission;

namespace ONEVO.Application.Features.Auth.Roles;

internal static class RolePermissionAssignability
{
    public static async Task<Result<IReadOnlyList<AuthPermission>>> ValidateForTenantAsync(
        Guid tenantId,
        IReadOnlyList<Guid> permissionIds,
        IPermissionRepository permissions,
        IModuleEntitlementService entitlements,
        CancellationToken ct)
    {
        if (permissionIds.Count == 0)
            return Result<IReadOnlyList<AuthPermission>>.Success([]);

        var resolved = await permissions.GetByIdsAsync(permissionIds, ct);
        if (resolved.Count != permissionIds.Count)
        {
            var missing = permissionIds.Except(resolved.Select(p => p.Id)).ToList();
            return Result<IReadOnlyList<AuthPermission>>.Failure(
                $"Unknown permission ids: {string.Join(", ", missing)}.");
        }

        var allowed = await entitlements.GetAssignablePermissionsForTenantAsync(
            tenantId,
            permissionIds,
            ct);

        if (allowed.Count == permissionIds.Count)
        {
            var ordered = resolved
                .OrderBy(p => p.Module, StringComparer.Ordinal)
                .ThenBy(p => p.Code, StringComparer.Ordinal)
                .ToList();
            return Result<IReadOnlyList<AuthPermission>>.Success(ordered);
        }

        var allowedIds = allowed.Select(p => p.Id).ToHashSet();
        var rejectedCodes = resolved
            .Where(p => !allowedIds.Contains(p.Id))
            .Select(p => p.Code)
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        return Result<IReadOnlyList<AuthPermission>>.Failure(
            $"Permissions are outside this tenant's active modules or are not assignable: {string.Join(", ", rejectedCodes)}");
    }
}
