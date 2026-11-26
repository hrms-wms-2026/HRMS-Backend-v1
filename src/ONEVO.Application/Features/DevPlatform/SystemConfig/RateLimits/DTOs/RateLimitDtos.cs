namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;

/// <summary>Per minute and burst. Burst is null for the sliding-window limits.</summary>
public sealed record RateLimitValueDto(int PerMinute, int? Burst);

/// <summary>The allowed range, so the admin screen can show it. Burst bounds are null when there is no burst.</summary>
public sealed record RateLimitBoundsDto(int MinPerMinute, int MaxPerMinute, int? MinBurst, int? MaxBurst);

/// <summary>A stored override. <see cref="RowVersion"/> goes back on the next PUT or DELETE.</summary>
public sealed record RateLimitOverrideDto(
    int PerMinute,
    int? Burst,
    DateTimeOffset UpdatedAt,
    Guid UpdatedByPlatformUserId,
    string RowVersion);

/// <summary>One limit on the platform screen.</summary>
public sealed record GlobalRateLimitDto(
    string LimitName,
    string Algorithm,
    RateLimitOverrideDto? Override,
    RateLimitValueDto AppSettings,
    RateLimitValueDto Effective,
    string EffectiveSource,
    RateLimitBoundsDto Bounds);

/// <summary>One per-tenant limit on a plan: its override and what it inherits from global.</summary>
public sealed record PlanRateLimitDto(
    string LimitName,
    string Algorithm,
    RateLimitOverrideDto? Override,
    RateLimitValueDto Inherited,
    string InheritedSource,
    RateLimitValueDto Effective,
    string EffectiveSource,
    RateLimitBoundsDto Bounds);

public sealed record PlanRateLimitsDto(Guid SubscriptionPlanId, IReadOnlyList<PlanRateLimitDto> Limits);

/// <summary>One per-tenant limit on a tenant. Source is tenant, plan, global or appsettings.</summary>
public sealed record TenantRateLimitDto(
    string LimitName,
    string Algorithm,
    RateLimitOverrideDto? Override,
    RateLimitValueDto Effective,
    string EffectiveSource,
    RateLimitBoundsDto Bounds);

public sealed record TenantRateLimitsDto(Guid TenantId, Guid? SubscriptionPlanId, IReadOnlyList<TenantRateLimitDto> Limits);

/// <summary>One history row. Old values are null for a new override, new values null for a removal.</summary>
public sealed record RateLimitChangeDto(
    Guid Id,
    DateTimeOffset ChangedAt,
    Guid PlatformUserId,
    string? PlatformUserName,
    string Scope,
    Guid? SubscriptionPlanId,
    Guid? TenantId,
    string LimitName,
    int? OldPerMinute,
    int? OldBurst,
    int? NewPerMinute,
    int? NewBurst,
    string Reason,
    string? CorrelationId);

public static class RateLimitAlgorithms
{
    public const string TokenBucket = "token_bucket";
    public const string SlidingWindow = "sliding_window";
}
