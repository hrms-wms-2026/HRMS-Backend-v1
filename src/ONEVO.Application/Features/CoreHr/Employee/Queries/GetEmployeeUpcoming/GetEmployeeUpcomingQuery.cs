using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeUpcoming;

public sealed record GetEmployeeUpcomingQuery(Guid EmployeeId, int Days = 14)
    : IRequest<Result<EmployeeUpcomingResponse>>;
