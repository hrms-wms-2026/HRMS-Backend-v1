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
    int? UnpaidGracePeriodDays = null) : IRequest<Result<SubscriptionPlanDetailDto>>, IAuditedCommand<Result<SubscriptionPlanDetailDto>>
{
    public AuditEntry DescribeAudit(Result<SubscriptionPlanDetailDto>? response)
        => AuditEntry.For("subscription_plan.updated", "subscription_plan", PlanId, null, new { Name, Tier, CompanySizeRange, ModuleKeys, Currency, OverrideMonthlyPrice, OverrideAnnualPrice, AiTokenLimitPerMonth, TrialPeriodDays, UnpaidGracePeriodDays });
}
