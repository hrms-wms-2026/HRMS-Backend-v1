using FluentValidation;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Validation;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.SetGlobalRateLimit;

public sealed class SetGlobalRateLimitCommandValidator : AbstractValidator<SetGlobalRateLimitCommand>
{
    public SetGlobalRateLimitCommandValidator(IOptionsMonitor<RateLimitSettings> appSettings)
    {
        RateLimitCommandRules.AddSetRules(this, appSettings, perTenantOnly: false);
    }
}
