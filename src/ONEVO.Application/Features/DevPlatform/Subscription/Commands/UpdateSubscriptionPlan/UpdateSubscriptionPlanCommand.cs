using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Subscription.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Commands.UpdateSubscriptionPlan;

public record UpdateSubscriptionPlanCommand(
    Guid PlanId,
    string? Name = null,
    string? Tier = null,
    string? CompanySizeRange = null,
    IReadOnlyList<string>? ModuleKeys = null,
    string? Currency = null,
    decimal? OverrideMonthlyPrice = null,
    decimal? OverrideAnnualPrice = null,
    int? AiTokenLimitPerMonth = null,
    int? TrialPeriodDays = null,
    int? UnpaidGracePeriodDays = null) : IRequest<Result<SubscriptionPlanDetailDto>>, IPlatformAuditedCommand<Result<SubscriptionPlanDetailDto>>
{
    // Only the fields the caller set (null means unchanged).
    public AuditEntry DescribeAudit(Result<SubscriptionPlanDetailDto> response) => new()
    {
        Action = "subscription_plan.updated",
        TargetType = "subscription_plan",
        TargetId = PlanId.ToString(),
        After = new
        {
            name = Name,
            tier = Tier,
            companySizeRange = CompanySizeRange,
            moduleKeys = ModuleKeys,
            currency = Currency,
            overrideMonthlyPrice = OverrideMonthlyPrice,
            overrideAnnualPrice = OverrideAnnualPrice,
            aiTokenLimitPerMonth = AiTokenLimitPerMonth,
            trialPeriodDays = TrialPeriodDays,
            unpaidGracePeriodDays = UnpaidGracePeriodDays,
        },
    };
}
