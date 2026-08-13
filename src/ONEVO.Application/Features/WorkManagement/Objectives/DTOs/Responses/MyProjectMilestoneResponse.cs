namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record MyProjectMilestoneResponse(
    Guid ObjectiveId, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title,
    Guid OwnerId, string? OwnerName, Guid? ReportingManagerId, string? ReportingManagerName,
    DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, decimal CompletedHours,
    bool ObjectiveIsActive, bool IsAchieved, DateTimeOffset? AchievedAt,
    bool MembershipIsActive, DateTimeOffset? MembershipRemovedAt, bool IsOwner);
