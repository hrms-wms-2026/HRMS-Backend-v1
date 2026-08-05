namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record DepartmentArchiveBlockers(
    int ActiveSubdepartmentCount,
    int ActiveEmployeeCount,
    int ActivePositionCount,
    bool IsUsedAsParent,
    bool HasActiveEmployees,
    bool HasActivePositions,
    bool PositionDependencyCheckSupported);
