using System.Text.Json;
using ONEVO.Application.Common.Configuration;
using ONEVO.Domain.Features.SharedPlatform.RateLimiting;

namespace ONEVO.Infrastructure.Services.SharedPlatform.RateLimiting;

/// <summary>
/// Turns override rows into <see cref="RateLimitSettings"/> objects. Pure: always returns a new
/// object and never changes the base, because the limiter treats handed-out settings as read-only.
/// </summary>
internal static class RateLimitSnapshotBuilder
{
    /// <summary>The platform settings: the base with the global rows applied.</summary>
    public static RateLimitSettings Platform(RateLimitSettings baseSettings, IEnumerable<RateLimitOverrideRow> rows) =>
        Apply(baseSettings, rows.Where(r => r.Scope == RateLimitSettingRules.GlobalScope));

    /// <summary>
    /// A tenant's settings: base, then global rows, then its plan's rows, then its own rows; the last
    /// one wins. Plan and tenant rows count only for the three per-tenant limits.
    /// </summary>
    public static RateLimitSettings Tenant(
        RateLimitSettings baseSettings, IReadOnlyList<RateLimitOverrideRow> rows, Guid tenantId, Guid? planId)
    {
        var ordered = rows.Where(r => r.Scope == RateLimitSettingRules.GlobalScope)
            .Concat(rows.Where(r => r.Scope == RateLimitSettingRules.PlanScope && planId is not null
                && r.SubscriptionPlanId == planId && IsPerTenant(r)))
            .Concat(rows.Where(r => r.Scope == RateLimitSettingRules.TenantScope
                && r.TenantId == tenantId && IsPerTenant(r)));
        return Apply(baseSettings, ordered);
    }

    public static string Fingerprint(RateLimitSettings settings) => JsonSerializer.Serialize(settings);

    private static bool IsPerTenant(RateLimitOverrideRow row) =>
        RateLimitSettingRules.PerTenantLimitNames.Contains(row.LimitName);

    private static RateLimitSettings Apply(RateLimitSettings baseSettings, IEnumerable<RateLimitOverrideRow> rows)
    {
        var settings = JsonSerializer.Deserialize<RateLimitSettings>(JsonSerializer.Serialize(baseSettings))!;
        foreach (var row in rows)
        {
            if (!RateLimitBounds.ByLimit.TryGetValue(row.LimitName, out var bounds))
                continue;

            var perMinute = bounds.ClampPerMinute(row.PerMinute);
            var burst = bounds.ClampBurst(row.Burst);
            switch (row.LimitName)
            {
                case RateLimitSettingRules.Anonymous:
                    SetSliding(settings.Anonymous, perMinute);
                    break;
                case RateLimitSettingRules.AdminAnonymous:
                    SetSliding(settings.AdminAnonymous, perMinute);
                    break;
                case RateLimitSettingRules.SignedIn:
                    SetBucket(settings.SignedIn, perMinute, burst);
                    break;
                case RateLimitSettingRules.TrayDevice:
                    SetBucket(settings.TrayDevice, perMinute, burst);
                    break;
                case RateLimitSettingRules.TenantCeiling:
                    SetBucket(settings.TenantCeiling, perMinute, burst);
                    break;
                case RateLimitSettingRules.AdminSignedIn:
                    SetBucket(settings.AdminSignedIn, perMinute, burst);
                    break;
            }
        }

        return settings;
    }

    // PermitLimit = perMinute x WindowSeconds / 60, at least 1. The window stays the base one.
    private static void SetSliding(SlidingWindowLimit limit, int perMinute) =>
        limit.PermitLimit = PerPeriod(perMinute, limit.WindowSeconds);

    // TokenLimit = burst; TokensPerPeriod = perMinute x ReplenishmentPeriodSeconds / 60, at least 1.
    private static void SetBucket(TokenBucketLimit limit, int perMinute, int? burst)
    {
        if (burst is int value)
            limit.TokenLimit = value;
        limit.TokensPerPeriod = PerPeriod(perMinute, limit.ReplenishmentPeriodSeconds);
    }

    private static int PerPeriod(int perMinute, int periodSeconds) =>
        Math.Max(1, (int)Math.Round(perMinute * (double)periodSeconds / 60, MidpointRounding.AwayFromZero));
}
