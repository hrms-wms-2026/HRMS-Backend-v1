namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record PositionResponse(
    Guid Id,
    Guid LegalEntityId,
    Guid? DepartmentId,
    string Name,
    string? Code,
    string PositionType,
    int MaxOccupancy,
    Guid? ReportsToPositionId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? DepartmentName,
    string? ReportsToPositionName,
    int ChildCount);
