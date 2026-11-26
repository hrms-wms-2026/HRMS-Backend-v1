namespace ONEVO.Domain.Features.SharedPlatform.RateLimiting;

/// <summary>
/// The allowed values for rate limit overrides. The database check constraints are built from these,
/// so the code and the constraints cannot drift apart.
/// </summary>
public static class RateLimitSettingRules
{
    public const string GlobalScope = "global";
    public const string PlanScope = "plan";
    public const string TenantScope = "tenant";

    public static IReadOnlyList<string> Scopes { get; } = [GlobalScope, PlanScope, TenantScope];

    public const string Anonymous = "Anonymous";
    public const string SignedIn = "SignedIn";
    public const string TrayDevice = "TrayDevice";
    public const string TenantCeiling = "TenantCeiling";
    public const string AdminAnonymous = "AdminAnonymous";
    public const string AdminSignedIn = "AdminSignedIn";

    /// <summary>The limits an admin may override. PreAuthentication stays in appsettings only.</summary>
    public static IReadOnlyList<string> LimitNames { get; } =
        [Anonymous, SignedIn, TrayDevice, TenantCeiling, AdminAnonymous, AdminSignedIn];

    /// <summary>The only limits a plan or tenant row may change; the rest are platform-wide.</summary>
    public static IReadOnlyList<string> PerTenantLimitNames { get; } = [SignedIn, TrayDevice, TenantCeiling];

    /// <summary>Sliding-window limits. They have no burst.</summary>
    public static IReadOnlyList<string> SlidingWindowLimitNames { get; } = [Anonymous, AdminAnonymous];

    public const int MinPerMinute = 10;
    public const int MaxPerMinute = 200_000;
    public const int MinBurst = 1;
    public const int MaxBurst = 50_000;
}
