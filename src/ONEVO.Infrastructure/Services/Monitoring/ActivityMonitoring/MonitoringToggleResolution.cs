namespace ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;

/// <summary>
/// Pure resolution chain for monitoring capability enablement and numeric settings.
/// Priority: employee → role → position → department → tenant → safe default.
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
        bool? rolePolicy,
        bool? positionPolicy,
        bool? departmentPolicy,
        bool? tenantToggle)
    {
        if (employeeOverride.HasValue)
            return employeeOverride.Value;
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
        int? roleMinutes,
        int? positionMinutes,
        int? departmentMinutes,
        int? tenantMinutes)
    {
        if (employeeMinutes.HasValue)
            return employeeMinutes.Value;
        if (roleMinutes.HasValue)
            return roleMinutes.Value;
        if (positionMinutes.HasValue)
            return positionMinutes.Value;
        if (departmentMinutes.HasValue)
            return departmentMinutes.Value;
        return tenantMinutes ?? DefaultIdleThresholdMinutes;
    }
}
