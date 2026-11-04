using FluentValidation;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.Team.DTOs;

namespace ONEVO.Application.Features.TimeAttendance.Team.Queries;

/// <summary>Anchor is any date inside the wanted period (default: the legal entity's work date). A future anchor
/// is clamped to today.</summary>
public sealed record GetTeamWorkTaskHoursQuery(string Period, DateOnly? Anchor) : IRequest<Result<TeamWorkTaskHoursResponse>>;

public sealed class GetTeamWorkTaskHoursQueryValidator : AbstractValidator<GetTeamWorkTaskHoursQuery>
{
    public GetTeamWorkTaskHoursQueryValidator()
    {
        RuleFor(x => x.Period)
            .Must(p => p is "day" or "week" or "month")
            .WithMessage("period must be day, week or month.");
    }
}
