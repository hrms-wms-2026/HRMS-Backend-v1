using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Balance.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Balance.Queries.GetEmployeeTimeOff;

public sealed record GetEmployeeTimeOffQuery(Guid EmployeeId, int? Year)
    : IRequest<Result<EmployeeTimeOffResponse>>;
