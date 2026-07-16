using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Subscription.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Commands.CreateSubscriptionPlan;

public record CreateSubscriptionPlanCommand(
    string Name,
    string Code,
    string Tier,
    string CompanySizeRange,
    IReadOnlyList<string> ModuleKeys,
    string Currency,
    decimal? OverrideMonthlyPrice,
    decimal? OverrideAnnualPrice,
    int? AiTokenLimitPerMonth,
    int TrialPeriodDays,
    int UnpaidGracePeriodDays) : IRequest<Result<SubscriptionPlanDetailDto>>;
