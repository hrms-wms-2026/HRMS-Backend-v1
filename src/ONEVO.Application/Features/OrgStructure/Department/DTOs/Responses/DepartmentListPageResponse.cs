namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record DepartmentListPageResponse(
    IReadOnlyList<DepartmentListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
