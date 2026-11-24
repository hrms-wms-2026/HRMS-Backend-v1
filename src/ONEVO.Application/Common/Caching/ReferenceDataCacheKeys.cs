namespace ONEVO.Application.Common.Caching;

/// <summary>
/// Every reference-data cache key (improvements task 22, DEC-103/DEC-104). Reads (<see cref="ServiceInterfaces.ITenantCache"/>)
/// and invalidation (the reference-data save interceptor) both build keys here, so they cannot drift apart.
/// <para>
/// Layout: <c>ref:{name}:{tenantId}:{variant}</c> for tenant data and <c>ref:{name}:global:</c> for platform-wide data.
/// The name comes first so one change to a platform-wide table (a permission) can drop that entry for every tenant.
/// </para>
/// </summary>
public static class ReferenceDataCacheKeys
{
    public const string Root = "ref:";

    // Tenant-owned lookups.
    public const string ActiveModules = "active-modules";
    public const string AssignablePermissions = "assignable-permissions";
    public const string LeaveTypes = "leave-types";
    public const string WorkModes = "work-modes";
    public const string ClockInPolicies = "clock-in-policies";
    public const string HolidayCalendarSettings = "holiday-calendar-settings";
    public const string TaskCategories = "task-categories";
    public const string ProjectCategories = "project-categories";

    // Platform-wide lookups (no tenant column).
    public const string UniversalPermissions = "universal-permissions";
    public const string ActiveModuleCatalog = "active-module-catalog";

    /// <summary>The one key for a tenant lookup. The tenant id is mandatory; an empty id is refused.</summary>
    public static string ForTenant(Guid tenantId, string name, string variant = "")
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("A tenant reference-data key needs a tenant id.", nameof(tenantId));
        return $"{TenantPrefix(tenantId, name)}{variant}";
    }

    /// <summary>Every variant of <paramref name="name"/> for one tenant.</summary>
    public static string TenantPrefix(Guid tenantId, string name) => $"{NamePrefix(name)}{tenantId:N}:";

    /// <summary>Every entry of <paramref name="name"/>, for every tenant and the global entry.</summary>
    public static string NamePrefix(string name) => $"{Root}{name}:";

    public static string Global(string name) => $"{NamePrefix(name)}global:";
}
