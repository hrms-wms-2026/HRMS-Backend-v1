using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeHistory;

public sealed record GetEmployeeHistoryQuery(Guid EmployeeId, int Limit = 20)
    : IRequest<Result<EmployeeHistoryResponse>>;
