using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Queries;

public sealed record GetAttendanceTodayQuery : IRequest<Result<AttendanceTodayResponse>>;
public sealed record GetMyAttendanceHistoryQuery(DateOnly From, DateOnly To, PagedRequest Paging) : IRequest<Result<PagedResult<AttendanceHistoryRow>>>;
public sealed record GetCoveredAttendanceHistoryQuery(DateOnly From, DateOnly To, Guid? EmployeeId, PagedRequest Paging) : IRequest<Result<PagedResult<AttendanceHistoryRow>>>;
