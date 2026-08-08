namespace ONEVO.Api.Contracts.OrgStructure.Positions;

public record SetPositionAccessRequest(
    Guid RoleId,
    bool RequiresApproval);
