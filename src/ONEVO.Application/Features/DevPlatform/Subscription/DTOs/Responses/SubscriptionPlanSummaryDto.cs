namespace ONEVO.Application.Features.DevPlatform.Subscription.DTOs.Responses;

public record SubscriptionPlanSummaryDto(
    Guid Id,
    string Name,
    string Code,
    string Tier,
    string CompanySizeRange,
    decimal EffectiveMonthlyPrice,
    decimal EffectiveAnnualPrice,
    string Currency,
    bool IsActive);
