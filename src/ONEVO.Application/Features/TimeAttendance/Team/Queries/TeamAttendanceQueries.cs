using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.Team.DTOs;

namespace ONEVO.Application.Features.TimeAttendance.Team.Queries;

/// <summary>Mode is "week" or "month"; Anchor is any date in the period (default: today).</summary>
public sealed record GetTeamAttendanceSummaryQuery(string Mode, DateOnly? Anchor, Guid? DepartmentId)
    : IRequest<Result<TeamAttendanceSummaryResponse>>;

/// <summary>Sort is "name" (default) or "rate": best attendance rate first, employees with no
/// eligible days last. Every row carries its rate rank either way.</summary>
public sealed record GetTeamAttendanceEmployeesQuery(
    string Mode, DateOnly? Anchor, Guid? DepartmentId, string? Search, int Page, int PageSize, string? Sort = null)
    : IRequest<Result<PagedResult<TeamAttendanceEmployeeRow>>>;
