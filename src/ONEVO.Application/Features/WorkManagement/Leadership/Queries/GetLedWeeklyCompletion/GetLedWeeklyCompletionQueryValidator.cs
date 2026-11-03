using FluentValidation;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedWeeklyCompletion;

public sealed class GetLedWeeklyCompletionQueryValidator : AbstractValidator<GetLedWeeklyCompletionQuery>
{
    public GetLedWeeklyCompletionQueryValidator()
    {
        RuleFor(x => x.Weeks)
            .InclusiveBetween(LedCompletionWeeks.MinWeeks, LedCompletionWeeks.MaxWeeks)
            .WithMessage($"Weeks must be between {LedCompletionWeeks.MinWeeks} and {LedCompletionWeeks.MaxWeeks}.");
    }
}
