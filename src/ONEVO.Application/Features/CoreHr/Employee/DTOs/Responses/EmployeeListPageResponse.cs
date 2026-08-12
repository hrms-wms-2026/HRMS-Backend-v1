namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

public record EmployeeListPageResponse(
    IReadOnlyList<EmployeeListItemResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);
