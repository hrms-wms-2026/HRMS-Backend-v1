namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record DepartmentTreeResponse(
    IReadOnlyList<DepartmentTreeNodeResponse> TreeItems);
