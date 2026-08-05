namespace ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;

/// <summary>
/// Pure resolution chain for monitoring capability enablement.
/// Priority: employee → role → position → department → tenant → false.
/// </summary>
public static class MonitoringToggleResolution
{
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
}
