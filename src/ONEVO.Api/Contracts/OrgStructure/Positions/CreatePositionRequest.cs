namespace ONEVO.Api.Contracts.OrgStructure.Positions;

public record CreatePositionRequest(
    Guid DepartmentId,
    string Name,
    string Code,
    int MaxOccupancy,
    Guid? ReportsToPositionId);
