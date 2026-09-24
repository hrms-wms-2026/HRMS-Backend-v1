using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployee;

public record GetEmployeeQuery(Guid EmployeeId) : IRequest<Result<EmployeeListItemResponse>>;
