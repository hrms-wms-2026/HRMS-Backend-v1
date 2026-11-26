using ONEVO.Application.Common.Configuration;
using ONEVO.Domain.Features.SharedPlatform.RateLimiting;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;

/// <summary>The two numbers an admin edits for one limit. Burst is null for sliding-window limits.</summary>
public sealed record RateLimitValue(int PerMinute, int? Burst);

/// <summary>One stored override, with its concurrency token.</summary>
public sealed record RateLimitOverrideRecord(
    Guid Id,
    string Scope,
    Guid? SubscriptionPlanId,
    Guid? TenantId,
    string LimitName,
    int PerMinute,
    int? Burst,
    DateTimeOffset UpdatedAt,
    Guid UpdatedByPlatformUserId,
    string RowVersion);

/// <summary>An effective value and where it comes from: tenant, plan, global or appsettings.</summary>
public sealed record ResolvedRateLimit(RateLimitValue Value, string Source);

public static class RateLimitSources
{
    public const string Tenant = RateLimitSettingRules.TenantScope;
    public const string Plan = RateLimitSettingRules.PlanScope;
    public const string Global = RateLimitSettingRules.GlobalScope;
    public const string AppSettings = "appsettings";
}

/// <summary>
/// Works out effective limits the same way the task 46 provider does: appsettings, then the global
/// row, then the plan row, then the tenant row, the last one winning. Plan and tenant rows count only
/// for the three per-tenant limits, and stored values are clamped to <see cref="RateLimitBounds"/>.
/// </summary>
public static class RateLimitResolver
{
    /// <summary>The appsettings value of one limit, in admin units (per minute and burst).</summary>
    public static RateLimitValue FromAppSettings(RateLimitSettings settings, RateLimitName limitName) => limitName switch
    {
        RateLimitName.Anonymous => Sliding(settings.Anonymous),
        RateLimitName.AdminAnonymous => Sliding(settings.AdminAnonymous),
        RateLimitName.SignedIn => Bucket(settings.SignedIn),
        RateLimitName.TrayDevice => Bucket(settings.TrayDevice),
        RateLimitName.TenantCeiling => Bucket(settings.TenantCeiling),
        RateLimitName.AdminSignedIn => Bucket(settings.AdminSignedIn),
        _ => throw new ArgumentOutOfRangeException(nameof(limitName), limitName, null),
    };

    /// <summary>The token bucket refill period from appsettings, or null for a sliding-window limit.</summary>
    public static int? ReplenishmentPeriodSeconds(RateLimitSettings settings, RateLimitName limitName) => limitName switch
    {
        RateLimitName.SignedIn => settings.SignedIn.ReplenishmentPeriodSeconds,
        RateLimitName.TrayDevice => settings.TrayDevice.ReplenishmentPeriodSeconds,
        RateLimitName.TenantCeiling => settings.TenantCeiling.ReplenishmentPeriodSeconds,
        RateLimitName.AdminSignedIn => settings.AdminSignedIn.ReplenishmentPeriodSeconds,
        _ => null,
    };

    /// <summary>
    /// The effective value for a scope. Pass no plan or tenant for the platform value, a plan only for
    /// a plan's value, and the tenant with its plan for a tenant's value.
    /// </summary>
    public static ResolvedRateLimit Resolve(
        RateLimitName limitName,
        RateLimitSettings appSettings,
        IEnumerable<RateLimitOverrideRecord> rows,
        Guid? planId,
        Guid? tenantId)
    {
        var name = limitName.ToStoredName();
        var forLimit = rows.Where(r => r.LimitName == name).ToList();
        var bounds = limitName.Bounds();

        if (limitName.IsPerTenant())
        {
            if (tenantId is not null
                && forLimit.FirstOrDefault(r => r.Scope == RateLimitSettingRules.TenantScope && r.TenantId == tenantId) is { } tenantRow)
                return new(Clamp(tenantRow, bounds), RateLimitSources.Tenant);

            if (planId is not null
                && forLimit.FirstOrDefault(r => r.Scope == RateLimitSettingRules.PlanScope && r.SubscriptionPlanId == planId) is { } planRow)
                return new(Clamp(planRow, bounds), RateLimitSources.Plan);
        }

        if (forLimit.FirstOrDefault(r => r.Scope == RateLimitSettingRules.GlobalScope) is { } globalRow)
            return new(Clamp(globalRow, bounds), RateLimitSources.Global);

        return new(FromAppSettings(appSettings, limitName), RateLimitSources.AppSettings);
    }

    private static RateLimitValue Clamp(RateLimitOverrideRecord row, LimitBounds bounds) =>
        new(bounds.ClampPerMinute(row.PerMinute), bounds.ClampBurst(row.Burst));

    private static RateLimitValue Sliding(SlidingWindowLimit limit) =>
        new(PerMinute(limit.PermitLimit, limit.WindowSeconds), null);

    private static RateLimitValue Bucket(TokenBucketLimit limit) =>
        new(PerMinute(limit.TokensPerPeriod, limit.ReplenishmentPeriodSeconds), limit.TokenLimit);

    private static int PerMinute(int perPeriod, int periodSeconds) =>
        periodSeconds <= 0 ? perPeriod : (int)Math.Round(perPeriod * 60.0 / periodSeconds, MidpointRounding.AwayFromZero);
}
