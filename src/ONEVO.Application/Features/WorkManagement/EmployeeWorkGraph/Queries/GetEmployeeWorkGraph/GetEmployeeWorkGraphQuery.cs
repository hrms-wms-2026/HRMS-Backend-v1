using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.Queries.GetEmployeeWorkGraph;

public sealed record GetEmployeeWorkGraphQuery(Guid EmployeeId) : IRequest<Result<EmployeeWorkGraphResponse>>;
