namespace ONEVO.Api.Contracts.OrgStructure.Positions;

public record UpdatePositionRequest(
    Guid DepartmentId,
    string Name,
    string Code,
    string PositionType,
    int MaxOccupancy,
    Guid? ReportsToPositionId);
