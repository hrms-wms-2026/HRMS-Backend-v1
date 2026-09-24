namespace ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;

/// <summary>
/// Pure resolution chain for monitoring capability enablement and numeric settings.
/// Priority: employee → work mode → role → position → department → tenant → safe default.
/// </summary>
public static class MonitoringToggleResolution
{
    /// <summary>
    /// Minutes of continuous inactivity before the TrayApp prompts for a screenshot, used when
    /// no tenant/policy/employee row has configured a value yet.
    /// </summary>
    public const int DefaultIdleThresholdMinutes = 2;

    public static bool Resolve(
        bool? employeeOverride,
        bool? workModeOverride,
        bool? rolePolicy,
        bool? positionPolicy,
        bool? departmentPolicy,
        bool? tenantToggle)
    {
        if (employeeOverride.HasValue)
            return employeeOverride.Value;
        if (workModeOverride.HasValue)
            return workModeOverride.Value;
        if (rolePolicy.HasValue)
            return rolePolicy.Value;
        if (positionPolicy.HasValue)
            return positionPolicy.Value;
        if (departmentPolicy.HasValue)
            return departmentPolicy.Value;
        return tenantToggle ?? false;
    }

    public static int ResolveMinutes(
        int? employeeMinutes,
        int? workModeMinutes,
        int? roleMinutes,
        int? positionMinutes,
        int? departmentMinutes,
        int? tenantMinutes)
    {
        if (employeeMinutes.HasValue)
            return employeeMinutes.Value;
        if (workModeMinutes.HasValue)
            return workModeMinutes.Value;
        if (roleMinutes.HasValue)
            return roleMinutes.Value;
        if (positionMinutes.HasValue)
            return positionMinutes.Value;
        if (departmentMinutes.HasValue)
            return departmentMinutes.Value;
        return tenantMinutes ?? DefaultIdleThresholdMinutes;
    }

    /// <summary>
    /// Same employee → work mode → role → position → department → tenant chain as
    /// <see cref="ResolveMinutes"/>, but for the nullable geofence radius: unlike the idle
    /// threshold, there is no safe-default fallback - null means "no tier configured a radius",
    /// which callers treat as "no proximity restriction" rather than a missing value.
    /// </summary>
    public static int? ResolveRadiusMeters(
        int? employeeRadius,
        int? workModeRadius,
        int? roleRadius,
        int? positionRadius,
        int? departmentRadius,
        int? tenantRadius)
    {
        if (employeeRadius.HasValue)
            return employeeRadius.Value;
        if (workModeRadius.HasValue)
            return workModeRadius.Value;
        if (roleRadius.HasValue)
            return roleRadius.Value;
        if (positionRadius.HasValue)
            return positionRadius.Value;
        if (departmentRadius.HasValue)
            return departmentRadius.Value;
        return tenantRadius;
    }
}
