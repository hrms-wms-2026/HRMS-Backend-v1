using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;

namespace ONEVO.Application.Features.OrgStructure.Queries.ListDepartments;

public record ListDepartmentsQuery(
    Guid LegalEntityId,
    string? Search,
    bool IncludeInactive,
    Guid? ParentDepartmentId,
    string View,
    string SortBy,
    string SortDirection,
    int Page,
    int PageSize) : IRequest<Result<DepartmentListResult>>;
