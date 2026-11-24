using FluentValidation;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Queries.GetRateLimitRejections;

public sealed class GetRateLimitRejectionsQueryValidator : AbstractValidator<GetRateLimitRejectionsQuery>
{
    public GetRateLimitRejectionsQueryValidator()
    {
        RuleFor(x => x.Minutes).InclusiveBetween(1, IRateLimitRejectionStats.MaxWindowMinutes);
    }
}
