using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.Team.DTOs;

namespace ONEVO.Application.Features.TimeAttendance.Team.Queries;

/// <summary>Mode is "week" or "month"; Anchor is any date in the period (default: today).</summary>
public sealed record GetTeamAttendanceSummaryQuery(string Mode, DateOnly? Anchor, Guid? DepartmentId)
    : IRequest<Result<TeamAttendanceSummaryResponse>>;

public sealed record GetTeamAttendanceEmployeesQuery(
    string Mode, DateOnly? Anchor, Guid? DepartmentId, string? Search, int Page, int PageSize)
    : IRequest<Result<PagedResult<TeamAttendanceEmployeeRow>>>;
