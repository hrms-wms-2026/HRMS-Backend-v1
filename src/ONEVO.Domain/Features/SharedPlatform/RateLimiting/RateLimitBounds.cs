namespace ONEVO.Domain.Features.SharedPlatform.RateLimiting;

/// <summary>Floor and ceiling of one limit. Burst bounds are null for sliding-window limits.</summary>
public sealed record LimitBounds(int MinPerMinute, int MaxPerMinute, int? MinBurst, int? MaxBurst)
{
    public int ClampPerMinute(int perMinute) => Math.Clamp(perMinute, MinPerMinute, MaxPerMinute);

    public int? ClampBurst(int? burst) =>
        burst is int value && MinBurst is int min && MaxBurst is int max ? Math.Clamp(value, min, max) : null;
}

/// <summary>
/// The tighter per-limit bounds from the admin-managed rate limits guide, section 4. The database
/// check constraint (<see cref="RateLimitSettingRules"/>) is only an absolute safety net; these are
/// what the provider clamps to (task 46) and what the admin API validates against (task 47). The
/// floors keep an admin from locking anyone out, the admin app included.
/// </summary>
public static class RateLimitBounds
{
    public static IReadOnlyDictionary<string, LimitBounds> ByLimit { get; } = new Dictionary<string, LimitBounds>
    {
        [RateLimitSettingRules.Anonymous] = new(60, 1_200, null, null),
        [RateLimitSettingRules.SignedIn] = new(120, 3_000, 30, 500),
        [RateLimitSettingRules.TrayDevice] = new(60, 1_200, 20, 300),
        [RateLimitSettingRules.TenantCeiling] = new(1_200, 120_000, 500, 20_000),
        [RateLimitSettingRules.AdminAnonymous] = new(60, 600, null, null),
        [RateLimitSettingRules.AdminSignedIn] = new(120, 2_400, 30, 300),
    };

    public static LimitBounds For(string limitName) => ByLimit[limitName];
}
