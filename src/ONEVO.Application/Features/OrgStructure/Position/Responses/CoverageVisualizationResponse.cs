namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

/// <summary>
/// Read model used by the organization charts. It expands each active coverage rule into the
/// concrete positions and employees affected by that rule without claiming that those employees
/// will necessarily route every approval to the owner.
/// </summary>
public sealed record CoverageVisualizationResponse(
    Guid OwnerPositionId,
    int CoveredPositionCount,
    int CoveredEmployeeCount,
    int UnresolvedRuleCount,
    IReadOnlyList<CoverageVisualizationRuleResponse> Rules);

public sealed record CoverageVisualizationRuleResponse(
    Guid CoverageId,
    string CoveredTargetType,
    Guid? CoveredPositionId,
    Guid? CoveredDepartmentId,
    int OwnerOrder,
    string Source,
    bool IsLocked,
    string ResolutionStatus,
    string? ResolutionReason,
    Guid? ResolvedEmployeeId,
    IReadOnlyList<Guid> CoveredPositionIds,
    IReadOnlyList<Guid> CoveredEmployeeIds);
