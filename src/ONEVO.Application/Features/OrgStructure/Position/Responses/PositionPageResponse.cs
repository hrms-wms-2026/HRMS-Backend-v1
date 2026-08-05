namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record PositionPageResponse(
    IReadOnlyList<PositionListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
