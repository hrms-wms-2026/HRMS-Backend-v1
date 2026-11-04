using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Planner.TeamSchedule;

/// <summary>GET /planner/team/week?weekStart=: the caller's reports' week, planned vs actual (the caller's current week when
/// no weekStart). weekStart must be a Monday; a week starting after the caller's own today is 400 planner_not_today.</summary>
public sealed record GetTeamScheduleWeekQuery(DateOnly? WeekStart) : IRequest<Result<TeamScheduleWeekResponse>>;
