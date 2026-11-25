namespace ONEVO.Domain.Features.SharedPlatform.RateLimiting.Entities;

/// <summary>
/// Append-only history of rate limit overrides (Improvements task 45). Old values are null when an
/// override is added, new values are null when it is removed. Rows are never updated or deleted.
/// </summary>
public class RateLimitSettingChange
{
    public Guid Id { get; set; }

    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid PlatformUserId { get; set; }

    public string Scope { get; set; } = RateLimitSettingRules.GlobalScope;

    public Guid? SubscriptionPlanId { get; set; }

    public Guid? TenantId { get; set; }

    public string LimitName { get; set; } = string.Empty;

    public int? OldPerMinute { get; set; }

    public int? OldBurst { get; set; }

    public int? NewPerMinute { get; set; }

    public int? NewBurst { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string? CorrelationId { get; set; }
}
