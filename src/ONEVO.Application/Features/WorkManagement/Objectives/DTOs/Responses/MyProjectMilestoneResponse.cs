namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record MyProjectMilestoneResponse(
    Guid ObjectiveId, Guid ProjectId, Guid? ParentObjectiveId, bool IsDefault, string Title,
    Guid OwnerId, string? OwnerName, Guid? ReportingManagerId, string? ReportingManagerName,
    DateOnly StartDate, DateOnly EndDate, decimal AllocatedHours, decimal CompletedHours,
    bool ObjectiveIsActive, bool IsAchieved, DateTimeOffset? AchievedAt,
    bool MembershipIsActive, DateTimeOffset? MembershipRemovedAt,
    /// <summary>Despite the name, this is IsEffectiveManagerAsync (owner OR active member,
    /// cascading up ancestors) - most WM actions (create task, move status, assign, settings
    /// visibility, ...) intentionally allow any effective manager, not just the true owner.</summary>
    bool IsOwner,
    /// <summary>True only for the actual owner (or an ancestor's owner) - never a plain member.
    /// Use this, not IsOwner, wherever the gate must match EditTaskCommandHandler /
    /// Approve|RejectTaskEditRequestCommandHandler's IsEffectiveOwnerAsync check (e.g. deciding
    /// whether a task edit saves directly or must go through an edit request).</summary>
    bool IsEffectiveOwner);
