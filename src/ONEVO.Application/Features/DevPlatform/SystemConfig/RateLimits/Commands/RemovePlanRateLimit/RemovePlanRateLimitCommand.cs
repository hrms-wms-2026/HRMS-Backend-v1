using MediatR;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Validation;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.RemovePlanRateLimit;

/// <summary>Improvements task 47. Needs platform.system_config.manage.</summary>
public sealed record RemovePlanRateLimitCommand(
    Guid SubscriptionPlanId,
    RateLimitName LimitName,
    string Reason,
    string? RowVersion,
    string? CorrelationId) : IRequest<Result<PlanRateLimitsDto>>, IRemoveRateLimitCommand, IAuditedCommand<Result<PlanRateLimitsDto>>
{
    // Ids, the limit and the values only; the free-text reason stays in rate_limit_setting_changes.
    public AuditEntry DescribeAudit(Result<PlanRateLimitsDto>? response)
        => AuditEntry.For("rate_limit.removed", "subscription_plan", SubscriptionPlanId, null,
            new { Scope = "plan", LimitName = LimitName.ToStoredName() });
}
