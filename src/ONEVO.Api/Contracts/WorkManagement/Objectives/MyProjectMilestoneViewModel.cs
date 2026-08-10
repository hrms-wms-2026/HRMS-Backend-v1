namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record MyProjectMilestoneViewModel(
    Guid ObjectiveId, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title,
    Guid OwnerId, string? OwnerName, Guid? ReportingManagerId, string? ReportingManagerName,
    DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, decimal CompletedHours,
    bool ObjectiveIsActive, bool IsAchieved, DateTimeOffset? AchievedAt,
    bool MembershipIsActive, DateTimeOffset? MembershipRemovedAt);
