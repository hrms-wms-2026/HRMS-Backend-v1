namespace ONEVO.Application.Features.OrgStructure.DTOs.Responses;

public record PositionAccessTemplateResponse(
    Guid PositionId,
    Guid? RoleId,
    string? RoleName,
    bool RequiresApproval,
    bool IsActive);
