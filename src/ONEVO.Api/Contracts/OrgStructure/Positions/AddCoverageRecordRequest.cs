namespace ONEVO.Api.Contracts.OrgStructure.Positions;

public record AddCoverageRecordRequest(
    string CoveredTargetType,
    Guid? CoveredPositionId,
    Guid? CoveredDepartmentId,
    int OwnerOrder,
    Guid? ResponsibleEmployeeId);
