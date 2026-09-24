namespace ONEVO.Api.Contracts.OrgStructure.Departments;

public record UpdateDepartmentRequest(
    string Name,
    string? Code,
    Guid? ParentDepartmentId,
    Guid? HeadPositionId);
