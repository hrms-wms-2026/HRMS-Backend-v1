namespace ONEVO.Domain.Features.SharedPlatform.RateLimiting.Entities;

/// <summary>
/// One admin override of an API rate limit (Improvements task 45), for the whole platform, one
/// subscription plan or one tenant. An empty table means "use appsettings". There is no Enabled
/// column on purpose: the master switch stays in configuration, so no admin edit can turn the
/// limiter off.
/// </summary>
/// <remarks>
/// Not an ITenantOwnedEntity: <see cref="TenantId"/> is null for global and plan rows, and there is
/// no tenant query filter. The RLS policy (TenantOrSystem) is the guard.
/// </remarks>
public class RateLimitSetting
{
    public Guid Id { get; set; }

    /// <summary>One of <see cref="RateLimitSettingRules.Scopes"/>.</summary>
    public string Scope { get; set; } = RateLimitSettingRules.GlobalScope;

    /// <summary>Set only when <see cref="Scope"/> is "plan".</summary>
    public Guid? SubscriptionPlanId { get; set; }

    /// <summary>Set only when <see cref="Scope"/> is "tenant".</summary>
    public Guid? TenantId { get; set; }

    /// <summary>A RateLimitSettings property name as written, for example "SignedIn".</summary>
    public string LimitName { get; set; } = string.Empty;

    /// <summary>Sustained requests per minute.</summary>
    public int PerMinute { get; set; }

    /// <summary>Token bucket size. Null for the sliding-window limits (Anonymous, AdminAnonymous).</summary>
    public int? Burst { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid UpdatedByPlatformUserId { get; set; }
}
