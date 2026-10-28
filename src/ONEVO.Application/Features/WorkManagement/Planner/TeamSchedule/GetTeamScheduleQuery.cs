using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Planner.TeamSchedule;

/// <summary>GET /planner/team?date=: the caller's reports' day (each person's local today when no date). A date after the
/// caller's own today is 400 planner_not_today.</summary>
public sealed record GetTeamScheduleQuery(DateOnly? Date) : IRequest<Result<TeamScheduleResponse>>;
