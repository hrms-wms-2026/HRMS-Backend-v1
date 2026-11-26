using FluentValidation;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Validation;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.SetPlanRateLimit;

public sealed class SetPlanRateLimitCommandValidator : AbstractValidator<SetPlanRateLimitCommand>
{
    public SetPlanRateLimitCommandValidator(IOptionsMonitor<RateLimitSettings> appSettings)
    {
        RuleFor(x => x.SubscriptionPlanId).NotEmpty();
        RateLimitCommandRules.AddSetRules(this, appSettings, perTenantOnly: true);
    }
}
