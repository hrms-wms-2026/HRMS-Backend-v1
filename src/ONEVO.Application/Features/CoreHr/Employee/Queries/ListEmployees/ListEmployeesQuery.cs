using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.ListEmployees;

public record ListEmployeesQuery(
    string? Search,
    Guid? DepartmentId,
    Guid? LegalEntityId,
    int Page = 1,
    int PageSize = 25) : IRequest<Result<EmployeeListPageResponse>>;
