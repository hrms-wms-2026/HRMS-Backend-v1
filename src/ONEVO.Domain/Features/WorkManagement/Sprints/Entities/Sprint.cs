using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

public static class SprintStatuses
{
    public const string Future = "future";
    public const string Active = "active";
    public const string Complete = "complete";
    public const string Incomplete = "incomplete";
    public const string Achieved = "achieved";
}

/// <summary>
/// A time-boxed iteration owned by one Objective. Achieved is a status value, not a use of
/// BaseEntity.IsDeleted - an Achieved sprint must stay visible to the owner's "all sprints" Backlog
/// view and to the Objective-achieve gate check (see AchieveObjectiveCommandHandler), both of which
/// would silently break under the standard !IsDeleted repository filter convention.
/// </summary>
public class Sprint : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid ObjectiveId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Status { get; set; } = SprintStatuses.Future;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? AchievedAt { get; set; }

    /// <summary>
    /// Set by SetSprintStatusCommand (an owner picking any status directly, bypassing the normal
    /// gates). Once true, SprintLifecycleJob's periodic sweep (EfSprintRepository.GetByStatusAsync)
    /// permanently excludes this sprint - a manual override must stick, not get silently flipped
    /// back by the next date-driven tick.
    /// </summary>
    public bool IsManuallyOverridden { get; set; }
}
