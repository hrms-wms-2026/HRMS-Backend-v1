namespace ONEVO.Api.Contracts.OrgStructure.Positions;

public record ActiveHolderViewModel(Guid EmployeeId, string FirstName, string LastName, string WorkEmail, Guid? AvatarFileId);
