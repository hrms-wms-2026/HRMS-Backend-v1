using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeDetail;

public sealed record GetEmployeeDetailQuery(Guid EmployeeId) : IRequest<Result<EmployeeDetailResponse>>;
