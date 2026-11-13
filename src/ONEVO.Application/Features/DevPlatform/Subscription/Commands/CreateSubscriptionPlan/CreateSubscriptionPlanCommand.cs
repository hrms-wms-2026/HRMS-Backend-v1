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
    int UnpaidGracePeriodDays) : IRequest<Result<SubscriptionPlanDetailDto>>, IAuditedCommand<Result<SubscriptionPlanDetailDto>>
{
    public AuditEntry DescribeAudit(Result<SubscriptionPlanDetailDto>? response)
        => AuditEntry.For("subscription_plan.created", "subscription_plan", response?.Value?.Id, null, new { Name, Code, Tier, CompanySizeRange, ModuleKeys, Currency, OverrideMonthlyPrice, OverrideAnnualPrice, AiTokenLimitPerMonth, TrialPeriodDays, UnpaidGracePeriodDays });
}
