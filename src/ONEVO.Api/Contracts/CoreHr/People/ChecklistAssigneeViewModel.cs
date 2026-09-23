namespace ONEVO.Api.Contracts.CoreHr.People;

public sealed record ChecklistAssigneeViewModel(
    Guid EmployeeId,
    Guid UserId,
    string DisplayName,
    string WorkEmail,
    Guid? AvatarFileId);
