using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeRecentActivity;

public sealed record GetEmployeeRecentActivityQuery(Guid EmployeeId, DateTimeOffset? Before = null, int Limit = 20)
    : IRequest<Result<EmployeeRecentActivityResponse>>;
