namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record PositionTreeNodeResponse(
    Guid Id,
    Guid LegalEntityId,
    Guid? DepartmentId,
    string Name,
    string? Code,
    string PositionType,
    int MaxOccupancy,
    Guid? ReportsToPositionId,
    bool IsActive,
    int ChildCount,
    IReadOnlyList<PositionTreeNodeResponse> Children,
    int AssignedCount,
    IReadOnlyList<PositionOccupantPreviewResponse> OccupantPreview,
    int RemainingAssignedCount);
