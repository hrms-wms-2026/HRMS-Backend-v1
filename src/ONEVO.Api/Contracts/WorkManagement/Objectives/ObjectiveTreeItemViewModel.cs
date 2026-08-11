namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record ObjectiveTreeItemViewModel(
    Guid Id, Guid? ParentObjectiveId, bool IsDefault, string Title, Guid OwnerId,
    DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, decimal CompletedHours, bool IsActive, bool IsAchieved);
