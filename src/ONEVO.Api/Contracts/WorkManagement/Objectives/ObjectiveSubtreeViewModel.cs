namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

public sealed record ObjectiveSubtreeViewModel(ObjectiveDetailViewModel? ParentObjective, ObjectiveSubtreeNodeViewModel Objective);

public sealed record ObjectiveSubtreeNodeViewModel(
    Guid Id, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title, string? Description,
    Guid OwnerId, Guid? ReportingManagerId, Guid CreatedById, DateOnly StartDate, DateOnly EndDate,
    decimal Progress, decimal? ActualHours, decimal AllocatedHours, decimal CompletedHours,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    string? OwnerName, string? ReportingManagerName, bool IsOwner, bool IsAchieved, DateTimeOffset? AchievedAt,
    IReadOnlyList<ObjectiveSubtreeNodeViewModel> Children);
