namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

// Deliberately omits DepartmentName/ReportsToPositionName/ChildCount: populating them per
// row would require an extra query per row (N+1) in ListPositionsQueryHandler. PositionResponse
// (single-item GetPositionByIdQuery) and PositionTreeNodeResponse (already has the full set
// loaded in memory) populate the richer fields cheaply; the paginated list does not.
// CurrentOccupancy/CurrentOccupancyCheckSupported are left as their long-standing (null, false)
// placeholder here deliberately, even though position_assignments now exists and
// AssignedCount below is populated from it: PositionResponse (GetPositionByIdQuery) still
// reports this pair as unsupported, and populating only the list response would make the same
// position report contradictory occupancy-support between endpoints. AssignedCount/
// OccupantPreview/RemainingAssignedCount are the real, populated successor fields; a follow-up
// pass should retire CurrentOccupancy/CurrentOccupancyCheckSupported everywhere (including
// PositionResponse and PositionArchiveBlockers.ActiveOccupants) rather than dual-write both.
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
    DateTimeOffset? UpdatedAt,
    int? CurrentOccupancy,
    bool CurrentOccupancyCheckSupported,
    int AssignedCount,
    IReadOnlyList<PositionOccupantPreviewResponse> OccupantPreview,
    int RemainingAssignedCount);
