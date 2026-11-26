using ONEVO.Domain.Features.SharedPlatform.RateLimiting;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;

/// <summary>
/// The six limits an admin may change (Improvements task 47). A closed list: there is no name for
/// login, MFA or password reset (AuthRateLimitingMiddleware stays code-fixed) and none for the
/// master switch <c>RateLimiting:Enabled</c>, which stays in appsettings only.
/// </summary>
public enum RateLimitName
{
    Anonymous,
    SignedIn,
    TrayDevice,
    TenantCeiling,
    AdminAnonymous,
    AdminSignedIn,
}

public static class RateLimitNames
{
    private static readonly IReadOnlyDictionary<string, RateLimitName> ByName =
        Enum.GetValues<RateLimitName>().ToDictionary(v => v.ToString(), v => v, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parses a route value by name only (case-insensitive). Unlike <c>Enum.TryParse</c> it never
    /// accepts a number, so "7" cannot become an undefined enum value.
    /// </summary>
    public static bool TryParse(string? value, out RateLimitName limitName)
    {
        limitName = default;
        return !string.IsNullOrWhiteSpace(value) && ByName.TryGetValue(value.Trim(), out limitName);
    }

    /// <summary>The stored name, as written in <see cref="RateLimitSettingRules.LimitNames"/>.</summary>
    public static string ToStoredName(this RateLimitName limitName) => limitName.ToString();

    public static bool IsPerTenant(this RateLimitName limitName) =>
        RateLimitSettingRules.PerTenantLimitNames.Contains(limitName.ToStoredName());

    public static bool IsSlidingWindow(this RateLimitName limitName) =>
        RateLimitSettingRules.SlidingWindowLimitNames.Contains(limitName.ToStoredName());

    public static LimitBounds Bounds(this RateLimitName limitName) => RateLimitBounds.For(limitName.ToStoredName());

    public static IReadOnlyList<RateLimitName> All { get; } = Enum.GetValues<RateLimitName>();

    public static IReadOnlyList<RateLimitName> PerTenant { get; } = All.Where(IsPerTenant).ToList();
}
