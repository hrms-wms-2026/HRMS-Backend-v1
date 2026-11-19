using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeChecklistOverview;

public sealed record GetEmployeeChecklistOverviewQuery(Guid EmployeeId)
    : IRequest<Result<EmployeeChecklistOverviewResponse>>;
