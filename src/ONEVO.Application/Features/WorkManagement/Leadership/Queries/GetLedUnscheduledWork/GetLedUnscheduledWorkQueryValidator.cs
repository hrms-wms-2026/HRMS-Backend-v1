using FluentValidation;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedUnscheduledWork;

public sealed class GetLedUnscheduledWorkQueryValidator : AbstractValidator<GetLedUnscheduledWorkQuery>
{
    public static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["all"] = "all",
        [LedTimeProgressCalculator.NoPlanKind] = LedTimeProgressCalculator.NoPlanKind,
        [LedTimeProgressCalculator.OutsideSlotKind] = LedTimeProgressCalculator.OutsideSlotKind,
    };

    public GetLedUnscheduledWorkQueryValidator()
    {
        RuleFor(x => x.Period)
            .Must(period => string.IsNullOrWhiteSpace(period) || LedProgressPeriod.IsWindowed(period))
            .WithMessage("Period must be 'week' or 'month'.");
        RuleFor(x => x.Kind)
            .Must(kind => LedProgressQueryOptions.IsBlankOr(kind, Kinds))
            .WithMessage("Kind must be one of: all, no_plan, outside_slot.");
    }
}
