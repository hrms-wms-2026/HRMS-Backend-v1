using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceOverview;

public sealed record GetEmployeeAttendanceOverviewQuery(Guid EmployeeId, DateOnly? From, DateOnly? To)
    : IRequest<Result<EmployeeAttendanceOverviewResponse>>;
