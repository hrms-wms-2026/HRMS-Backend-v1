namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record ManagementCoverageRecordResponse(
    Guid Id,
    Guid OwnerPositionId,
    string? OwnerPositionName,
    string CoveredTargetType,
    Guid? CoveredPositionId,
    string? CoveredPositionName,
    Guid? CoveredDepartmentId,
    string? CoveredDepartmentName,
    int OwnerOrder,
    string Source,
    bool IsLocked,
    string Status,
    Guid? ResponsibleEmployeeId,
    string? ResponsibleEmployeeName);
