using ONEVO.Domain.Features.OrgStructure.Entities;

namespace ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;

public sealed record DepartmentPage(
    IReadOnlyList<Department> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);
