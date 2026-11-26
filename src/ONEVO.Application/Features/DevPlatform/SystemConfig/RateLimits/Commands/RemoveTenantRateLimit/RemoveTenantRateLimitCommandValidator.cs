using FluentValidation;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Validation;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.RemoveTenantRateLimit;

public sealed class RemoveTenantRateLimitCommandValidator : AbstractValidator<RemoveTenantRateLimitCommand>
{
    public RemoveTenantRateLimitCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RateLimitCommandRules.AddRemoveRules(this, perTenantOnly: true);
    }
}
