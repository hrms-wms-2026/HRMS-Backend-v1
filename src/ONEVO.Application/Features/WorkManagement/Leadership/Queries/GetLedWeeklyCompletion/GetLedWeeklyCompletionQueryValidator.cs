using FluentValidation;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWeeklyCompletion;

public sealed class GetLedWeeklyCompletionQueryValidator : AbstractValidator<GetLedWeeklyCompletionQuery>
{
    public GetLedWeeklyCompletionQueryValidator()
    {
        // Weeks only sizes the default (no period) chart; with a period it is ignored.
        RuleFor(x => x.Weeks)
            .InclusiveBetween(LedCompletionWeeks.MinWeeks, LedCompletionWeeks.MaxWeeks)
            .When(x => string.IsNullOrWhiteSpace(x.Period))
            .WithMessage($"Weeks must be between {LedCompletionWeeks.MinWeeks} and {LedCompletionWeeks.MaxWeeks}.");

        RuleFor(x => x.Period)
            .Must(LedProgressPeriod.IsWindowed)
            .When(x => !string.IsNullOrWhiteSpace(x.Period))
            .WithMessage("Period must be 'week' or 'month'.");
    }
}
