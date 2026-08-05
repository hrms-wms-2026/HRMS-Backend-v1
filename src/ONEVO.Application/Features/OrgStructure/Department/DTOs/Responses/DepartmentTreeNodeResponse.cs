namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record DepartmentTreeNodeResponse(
    Guid Id,
    Guid LegalEntityId,
    string Name,
    string? Code,
    Guid? ParentDepartmentId,
    Guid? HeadPositionId,
    bool IsActive,
    IReadOnlyList<DepartmentTreeNodeResponse> Children);
