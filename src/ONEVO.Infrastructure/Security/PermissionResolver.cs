using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;

namespace ONEVO.Infrastructure.Security;

public class PermissionResolver : IPermissionResolver
{
    private readonly IPermissionRepository _permissions;
    private readonly IUserPermissionOverrideRepository _permissionOverrides;
    private readonly IModuleEntitlementService _entitlements;
    private readonly IDateTimeProvider _clock;

    public PermissionResolver(
        IPermissionRepository permissions,
        IUserPermissionOverrideRepository permissionOverrides,
        IModuleEntitlementService entitlements,
        IDateTimeProvider clock)
    {
        _permissions = permissions;
        _permissionOverrides = permissionOverrides;
        _entitlements = entitlements;
        _clock = clock;
    }

    public async Task<List<string>> ResolveAsync(Guid userId, Guid tenantId, Guid? activeLegalEntityId, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        if (await _permissions.UserHasPermissionCodeAsync(userId, "*", now, ct))
            return ["*"];

        var activeModuleKeys = await _entitlements.GetActiveModuleKeysForTenantAsync(tenantId, ct);
        var activeModules = activeModuleKeys.ToHashSet(StringComparer.Ordinal);

        // Platform/system capability modules (roles administration, tenant configuration
        // bootstrap, user administration, notifications administration) are never
        // subscribed product modules, so they never appear in activeModuleKeys. They must
        // still gate permissions open here - otherwise RolePermission rows DefaultRoleSeeder
        // grants for these modules (e.g. roles:read/roles:manage) would be silently
        // filtered out below. This union is local to gating; it does not affect the
        // active_modules API response, which is sourced from activeModuleKeys directly.
        activeModules.UnionWith(PlatformBaselineModules.Keys);

        var roleRows = await _permissions.ListRolePermissionCodesWithModulesAsync(userId, now, activeLegalEntityId, ct);
        var overrides = await _permissionOverrides.ListForUserAsync(tenantId, userId, ct);

        var grantCodes = overrides
            .Where(o => o.GrantType == "grant")
            .Select(o => o.Code)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var revoked = overrides
            .Where(o => o.GrantType == "revoke")
            .Select(o => o.Code)
            .ToHashSet(StringComparer.Ordinal);

        // Step 1: module auto-grants — only for active modules
        var effective = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in ModuleAutoGrants.GetForModules(activeModules))
            effective.Add(code);

        // Step 2: role permissions filtered by active modules
        foreach (var row in roleRows)
        {
            if (row.Code == "*") continue;
            if (activeModules.Contains(row.Module))
                effective.Add(row.Code);
        }

        // Step 3: override grants filtered by active modules
        if (grantCodes.Count > 0)
        {
            var resolvedGrants = await _permissions.GetByCodesAsync(grantCodes, ct);
            var byCode = resolvedGrants.ToDictionary(p => p.Code, StringComparer.Ordinal);
            foreach (var code in grantCodes)
            {
                if (byCode.TryGetValue(code, out var perm) && activeModules.Contains(perm.Module))
                    effective.Add(code);
            }
        }

        // Step 4: override revokes — cannot remove module auto-grants
        foreach (var code in revoked)
        {
            if (!ModuleAutoGrants.Contains(code))
                effective.Remove(code);
        }

        // Step 5: derive inbox:read and notifications:read
        if (effective.Overlaps(DerivedPermissions.InboxTriggers))
            effective.Add("inbox:read");
        if (effective.Overlaps(DerivedPermissions.NotificationTriggers))
            effective.Add("notifications:read");

        return [.. effective];
    }
}
