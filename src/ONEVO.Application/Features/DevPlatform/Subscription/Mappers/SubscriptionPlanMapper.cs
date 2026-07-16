using ONEVO.Application.Features.DevPlatform.Subscription.DTOs.Responses;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Mappers;

internal static class SubscriptionPlanMapper
{
    internal static SubscriptionPlanDetailDto ToDetailDto(SubscriptionPlan plan) =>
        new(
            plan.Id,
            plan.Name,
            plan.Code,
            plan.Tier,
            plan.CompanySizeRange,
            plan.PricingUnit,
            plan.GetIncludedModules(),
            plan.CalculatedMonthlyPrice,
            plan.CalculatedAnnualPrice,
            plan.OverrideMonthlyPrice,
            plan.OverrideAnnualPrice,
            plan.OverrideMonthlyPrice ?? plan.CalculatedMonthlyPrice,
            plan.OverrideAnnualPrice ?? plan.CalculatedAnnualPrice,
            plan.Currency,
            plan.AiTokenLimitPerMonth,
            plan.TrialPeriodDays,
            plan.UnpaidGracePeriodDays,
            plan.IsActive,
            plan.CreatedAt,
            plan.UpdatedAt);

    internal static SubscriptionPlanSummaryDto ToSummaryDto(SubscriptionPlan plan) =>
        new(
            plan.Id,
            plan.Name,
            plan.Code,
            plan.Tier,
            plan.CompanySizeRange,
            plan.OverrideMonthlyPrice ?? plan.CalculatedMonthlyPrice,
            plan.OverrideAnnualPrice ?? plan.CalculatedAnnualPrice,
            plan.Currency,
            plan.IsActive);
}
