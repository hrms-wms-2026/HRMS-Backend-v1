namespace ONEVO.Api.Contracts.OrgStructure.Departments;

public record CreateDepartmentRequest(
    string Name,
    string? Code,
    Guid? ParentDepartmentId,
    Guid? HeadPositionId);
