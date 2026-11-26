using FluentValidation;
using ONEVO.Domain.Features.SharedPlatform.RateLimiting;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Queries.GetRateLimitChanges;

public sealed class GetRateLimitChangesQueryValidator : AbstractValidator<GetRateLimitChangesQuery>
{
    public GetRateLimitChangesQueryValidator()
    {
        RuleFor(x => x.Scope)
            .Must(scope => RateLimitSettingRules.Scopes.Contains(scope!))
            .WithMessage("scope must be global, plan or tenant.")
            .When(x => !string.IsNullOrWhiteSpace(x.Scope));
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, GetRateLimitChangesQuery.MaxPageSize);
    }
}
