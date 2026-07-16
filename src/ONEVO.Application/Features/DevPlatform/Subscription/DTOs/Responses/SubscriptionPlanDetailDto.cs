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
    DateTimeOffset? UpdatedAt);
