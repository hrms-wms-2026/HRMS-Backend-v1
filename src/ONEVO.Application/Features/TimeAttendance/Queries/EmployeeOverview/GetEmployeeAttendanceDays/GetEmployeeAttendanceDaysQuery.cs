using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceDays;

public sealed record GetEmployeeAttendanceDaysQuery(Guid EmployeeId, DateOnly? From, DateOnly? To)
    : IRequest<Result<EmployeeAttendanceDaysResponse>>;
