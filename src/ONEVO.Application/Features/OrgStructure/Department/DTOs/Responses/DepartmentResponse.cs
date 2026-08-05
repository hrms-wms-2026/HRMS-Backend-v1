namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record DepartmentResponse(
    Guid Id,
    Guid LegalEntityId,
    string Name,
    string? Code,
    Guid? ParentDepartmentId,
    Guid? HeadPositionId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
