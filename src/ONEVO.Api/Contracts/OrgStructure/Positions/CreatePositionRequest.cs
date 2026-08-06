namespace ONEVO.Api.Contracts.OrgStructure.Positions;

public record CreatePositionRequest(
    Guid DepartmentId,
    string Name,
    string Code,
    string PositionType,
    int MaxOccupancy,
    Guid? ReportsToPositionId);
