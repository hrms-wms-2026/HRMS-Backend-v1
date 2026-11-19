using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceDiscipline;

public sealed record GetEmployeeAttendanceDisciplineQuery(Guid EmployeeId, DateOnly? From, DateOnly? To, string? Compare = null)
    : IRequest<Result<EmployeeAttendanceDisciplineResponse>>;
