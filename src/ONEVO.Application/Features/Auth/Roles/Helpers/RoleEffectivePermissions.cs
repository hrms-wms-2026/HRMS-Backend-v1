using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission;
using AuthPermission = ONEVO.Domain.Features.Auth.Entities.Permission;

namespace ONEVO.Application.Features.Auth.Roles;

/// <summary>
/// Narrows a role's stored permission rows to the ones it actually grants, using the same
/// rule as PermissionResolver: the permission's module is one the tenant subscribes to or a
/// platform baseline module, it is not the "*" wildcard, and it is not a module auto-grant. Rows for unsubscribed modules
/// stay in role_permissions (e.g. the Owner role is seeded with every module) but grant
/// nothing, so the role list count and the role detail must not report them.
/// </summary>
internal static class RoleEffectivePermissions
{
    public static async Task<IReadOnlySet<string>> GetGrantingModulesAsync(
        IModuleEntitlementService entitlements,
        Guid tenantId,
        CancellationToken ct)
    {
        var modules = (await entitlements.GetActiveModuleKeysForTenantAsync(tenantId, ct))
            .ToHashSet(StringComparer.Ordinal);
        modules.UnionWith(PlatformBaselineModules.Keys);
        return modules;
    }

    // Module auto-grants (e.g. activity:read:self) go to every user of the module, not to the
    // role, and the catalog hides them too, so they are not counted against the role.
    public static bool Grants(AuthPermission permission, IReadOnlySet<string> grantingModules) =>
        permission.Code != "*"
        && !ModuleAutoGrants.Contains(permission.Code)
        && grantingModules.Contains(permission.Module);

    public static IReadOnlyList<AuthPermission> Filter(
        IEnumerable<AuthPermission> permissions,
        IReadOnlySet<string> grantingModules) =>
        permissions.Where(p => Grants(p, grantingModules)).ToList();
}
