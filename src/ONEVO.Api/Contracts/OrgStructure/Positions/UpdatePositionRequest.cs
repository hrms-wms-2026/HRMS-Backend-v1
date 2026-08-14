namespace ONEVO.Api.Contracts.OrgStructure.Positions;

public record UpdatePositionRequest(
    Guid DepartmentId,
    string Name,
    string Code,
    int MaxOccupancy,
    Guid? ReportsToPositionId);
