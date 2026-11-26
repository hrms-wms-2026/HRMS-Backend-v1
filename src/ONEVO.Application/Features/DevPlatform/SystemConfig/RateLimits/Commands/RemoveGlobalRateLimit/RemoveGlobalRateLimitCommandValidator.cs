using FluentValidation;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Validation;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.RemoveGlobalRateLimit;

public sealed class RemoveGlobalRateLimitCommandValidator : AbstractValidator<RemoveGlobalRateLimitCommand>
{
    public RemoveGlobalRateLimitCommandValidator()
    {
        RateLimitCommandRules.AddRemoveRules(this, perTenantOnly: false);
    }
}
