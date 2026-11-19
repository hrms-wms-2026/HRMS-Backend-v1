using FluentValidation;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedPeriodProgress;

public sealed class GetLedPeriodProgressQueryValidator : AbstractValidator<GetLedPeriodProgressQuery>
{
    public GetLedPeriodProgressQueryValidator()
    {
        RuleFor(x => x.Period)
            .Must(LedProgressPeriod.IsWindowed)
            .WithMessage("Period must be 'week' or 'month'.");
    }
}
