using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;

namespace ONEVO.Application.Features.DevPlatform.Subscription.DTOs.Responses;

public record SubscriptionPlanDetailDto(
    Guid Id,
    string Name,
    string Code,
    string Tier,
    string CompanySizeRange,
    string PricingUnit,
    IReadOnlyList<string> IncludedModules,
    decimal CalculatedMonthlyPrice,
    decimal CalculatedAnnualPrice,
    decimal? OverrideMonthlyPrice,
    decimal? OverrideAnnualPrice,
    decimal EffectiveMonthlyPrice,
    decimal EffectiveAnnualPrice,
    string Currency,
    int? AiTokenLimitPerMonth,
    int TrialPeriodDays,
    int UnpaidGracePeriodDays,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    /// <summary>
    /// Read-only rate limit summary for the admin plan detail page (Improvements task 47). Filled only
    /// for the admin GET; null elsewhere. Changes go through admin/v1/subscription-plans/{id}/rate-limits.
    /// </summary>
    public IReadOnlyList<PlanRateLimitDto>? RateLimits { get; init; }
}
