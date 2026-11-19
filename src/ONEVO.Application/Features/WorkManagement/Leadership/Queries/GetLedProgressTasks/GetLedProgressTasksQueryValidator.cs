using FluentValidation;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Queries.GetLedProgressTasks;

public sealed class GetLedProgressTasksQueryValidator : AbstractValidator<GetLedProgressTasksQuery>
{
    public GetLedProgressTasksQueryValidator()
    {
        RuleFor(x => x.Search)
            .MaximumLength(LedProgressQueryOptions.MaxSearchLength)
            .WithMessage($"Search cannot exceed {LedProgressQueryOptions.MaxSearchLength} characters.");
        RuleFor(x => x.Status)
            .Must(status => LedProgressQueryOptions.IsBlankOr(status, LedProgressQueryOptions.Statuses))
            .WithMessage("Status must be one of: completed, inProgress, notStarted.");
        RuleFor(x => x.Sort)
            .Must(sort => LedProgressQueryOptions.IsBlankOr(sort, LedProgressQueryOptions.TaskSorts))
            .WithMessage("Sort must be one of: estimatedHours, dueDate, title, status.");
        RuleFor(x => x.Direction)
            .Must(LedProgressQueryOptions.IsDirection)
            .WithMessage("Direction must be 'asc' or 'desc'.");
        RuleFor(x => x.Period)
            .Must(period => LedProgressQueryOptions.IsBlankOr(period, LedProgressPeriod.Periods))
            .WithMessage("Period must be one of: all, week, month.");
    }
}
