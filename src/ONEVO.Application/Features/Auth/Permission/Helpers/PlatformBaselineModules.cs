namespace ONEVO.Application.Features.Auth.Permission;

/// <summary>
/// Platform/system capability module keys - never subscribed Phase 1 product package
/// modules, so they must never appear in a subscription plan's included_modules_json.
/// Every tenant's Owner role is granted the permissions these modules own unconditionally
/// (see DefaultRoleSeeder), and PermissionResolver treats them as always-active for the
/// purpose of resolving a user's effective permissions (see PermissionResolver), so that
/// baseline platform access (roles administration, tenant configuration bootstrap, user
/// administration, notification administration) never depends on which product modules a
/// tenant has subscribed to.
/// </summary>
public static class PlatformBaselineModules
{
    public static readonly string[] Keys = ["auth", "configuration", "roles", "notifications"];
}
