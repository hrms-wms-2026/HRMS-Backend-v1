using FluentValidation;

namespace ONEVO.Application.Features.TimeAttendance.Team.Queries;

public sealed class GetTeamWorkingHoursQueryValidator : AbstractValidator<GetTeamWorkingHoursQuery>
{
    public GetTeamWorkingHoursQueryValidator()
    {
        RuleFor(x => x.WeekStart)
            .Must(start => start is null || start.Value.DayOfWeek == DayOfWeek.Monday)
            .WithMessage("weekStart must be a Monday.");
    }
}
