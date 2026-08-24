namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record CoverageResolutionLevelResponse(
    int OwnerOrder,
    Guid OwnerPositionId,
    string? OwnerPositionName,
    string Status,
    Guid? EmployeeId,
    string? EmployeeName);
