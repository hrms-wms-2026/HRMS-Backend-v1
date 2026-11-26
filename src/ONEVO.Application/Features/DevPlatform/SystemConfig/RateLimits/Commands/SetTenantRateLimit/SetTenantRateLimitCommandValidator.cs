using FluentValidation;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Validation;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.SetTenantRateLimit;

public sealed class SetTenantRateLimitCommandValidator : AbstractValidator<SetTenantRateLimitCommand>
{
    public SetTenantRateLimitCommandValidator(IOptionsMonitor<RateLimitSettings> appSettings)
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RateLimitCommandRules.AddSetRules(this, appSettings, perTenantOnly: true);
    }
}
