using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeIdentity;

public sealed record GetEmployeeIdentityQuery(Guid EmployeeId) : IRequest<Result<EmployeeIdentityResponse>>;
