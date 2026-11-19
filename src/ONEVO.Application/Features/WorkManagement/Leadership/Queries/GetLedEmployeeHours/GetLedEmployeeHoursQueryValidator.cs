using FluentValidation;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedEmployeeHours;

public sealed class GetLedEmployeeHoursQueryValidator : AbstractValidator<GetLedEmployeeHoursQuery>
{
    public GetLedEmployeeHoursQueryValidator()
    {
        RuleFor(x => x.Search)
            .MaximumLength(LedProgressQueryOptions.MaxSearchLength)
            .WithMessage($"Search cannot exceed {LedProgressQueryOptions.MaxSearchLength} characters.");
        RuleFor(x => x.Sort)
            .Must(sort => LedProgressQueryOptions.IsBlankOr(sort, LedProgressQueryOptions.EmployeeSorts))
            .WithMessage("Sort must be one of: totalHours, completedHours, inProgressHours, notStartedHours, name.");
        RuleFor(x => x.Direction)
            .Must(LedProgressQueryOptions.IsDirection)
            .WithMessage("Direction must be 'asc' or 'desc'.");
        RuleFor(x => x.Period)
            .Must(period => LedProgressQueryOptions.IsBlankOr(period, LedProgressPeriod.Periods))
            .WithMessage("Period must be one of: all, week, month.");
    }
}
