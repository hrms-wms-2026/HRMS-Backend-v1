using FluentValidation;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Validation;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.RemovePlanRateLimit;

public sealed class RemovePlanRateLimitCommandValidator : AbstractValidator<RemovePlanRateLimitCommand>
{
    public RemovePlanRateLimitCommandValidator()
    {
        RuleFor(x => x.SubscriptionPlanId).NotEmpty();
        RateLimitCommandRules.AddRemoveRules(this, perTenantOnly: true);
    }
}
