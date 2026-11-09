using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.Team.DTOs;

namespace ONEVO.Application.Features.TimeAttendance.Team.Queries;

/// <param name="Status">on_track | raise_rate | overtime | no_history; null = all.</param>
/// <param name="Sort">overtime (default, most first) | requiredRate (highest first) | rate (lowest first, no history last) | name.</param>
/// <param name="Search">Employee name, case-insensitive.</param>
public sealed record GetTeamEmployeeForecastQuery(
    string? Status = null, string? Sort = null, string? Search = null, int Page = 1, int PageSize = 10)
    : IRequest<Result<TeamEmployeeForecastResponse>>;
