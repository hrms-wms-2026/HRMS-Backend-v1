using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Subscription.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

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
    int UnpaidGracePeriodDays) : IRequest<Result<SubscriptionPlanDetailDto>>, IPlatformAuditedCommand<Result<SubscriptionPlanDetailDto>>
{
    public AuditEntry DescribeAudit(Result<SubscriptionPlanDetailDto> response) => new()
    {
        Action = "subscription_plan.created",
        TargetType = "subscription_plan",
        TargetId = response.Value?.Id.ToString(),
        After = new
        {
            name = Name,
            code = Code,
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
