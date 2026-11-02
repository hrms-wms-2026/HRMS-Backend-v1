using FluentValidation;

namespace ONEVO.Application.Features.WorkManagement.Planner.TeamSchedule;

public sealed class GetTeamScheduleWeekQueryValidator : AbstractValidator<GetTeamScheduleWeekQuery>
{
    public GetTeamScheduleWeekQueryValidator()
    {
        RuleFor(x => x.WeekStart)
            .Must(weekStart => weekStart is null || weekStart.Value.DayOfWeek == DayOfWeek.Monday)
            .WithMessage("weekStart must be a Monday.");
    }
}
