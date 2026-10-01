using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeOverviewSignals;

public sealed record GetEmployeeOverviewSignalsQuery(Guid EmployeeId, DateOnly? From, DateOnly? To)
    : IRequest<Result<EmployeeOverviewSignalsResponse>>;
