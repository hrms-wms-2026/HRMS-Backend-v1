using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.ListEmployees;

public record ListEmployeesQuery(
    string? Search,
    Guid? DepartmentId,
    Guid? LegalEntityId,
    int Page = 1,
    int PageSize = 25,
    Guid? PositionId = null,
    IReadOnlyList<string>? EmploymentTypeCodes = null,
    Guid? ReportingManagerId = null,
    string? SortBy = null,
    bool SortDescending = false) : IRequest<Result<EmployeeListPageResponse>>;
    bool ActiveOnly = false) : IRequest<Result<EmployeeListPageResponse>>;
