using FluentValidation;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedTimeProgress;

public sealed class GetLedTimeProgressQueryValidator : AbstractValidator<GetLedTimeProgressQuery>
{
    public GetLedTimeProgressQueryValidator()
    {
        RuleFor(x => x.Period)
            .Must(LedProgressPeriod.IsWindowed)
            .WithMessage("Period must be 'week' or 'month'.");
    }
}
