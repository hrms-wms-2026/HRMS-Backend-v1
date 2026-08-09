namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

// Deliberately omits DepartmentName/ReportsToPositionName/ChildCount: populating them per
// row would require an extra query per row (N+1) in ListPositionsQueryHandler. PositionResponse
// (single-item GetPositionByIdQuery) and PositionTreeNodeResponse (already has the full set
// loaded in memory) populate the richer fields cheaply; the paginated list does not.
// CurrentOccupancy is the exception: it is always (null, false) here, same as every other
// Position response - no position_assignments table exists anywhere in this codebase, so the
// count is unmeasurable, not zero, and there is no per-row query to avoid (see
// PositionArchiveBlockers.ActiveOccupants for the same nullable-plus-supported-flag precedent).
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
    bool CurrentOccupancyCheckSupported);
