using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.Team.DTOs;

namespace ONEVO.Application.Features.TimeAttendance.Team.Queries;

/// <summary>WeekStart is an ISO Monday; default: the week of the legal entity's work date.</summary>
public sealed record GetTeamWorkingHoursQuery(DateOnly? WeekStart) : IRequest<Result<TeamWorkingHoursResponse>>;
