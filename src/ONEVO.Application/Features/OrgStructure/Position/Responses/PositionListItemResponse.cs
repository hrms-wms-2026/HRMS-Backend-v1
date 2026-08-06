namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

// Deliberately omits DepartmentName/ReportsToPositionName/ChildCount: populating them per
// row would require an extra query per row (N+1) in ListPositionsQueryHandler. PositionResponse
// (single-item GetPositionByIdQuery) and PositionTreeNodeResponse (already has the full set
// loaded in memory) populate the richer fields cheaply; the paginated list does not.
public record PositionListItemResponse(
    Guid Id,
    Guid LegalEntityId,
    Guid? DepartmentId,
    string Name,
    string? Code,
    string PositionType,
    int MaxOccupancy,
    Guid? ReportsToPositionId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
