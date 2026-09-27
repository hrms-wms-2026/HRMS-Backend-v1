using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

public static class SprintStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Complete = "complete";
    public const string Achieved = "achieved";
}

/// <summary>
/// A time-boxed, project-level bunch of tasks (spec 2026-09-23). Achieved is a status value, not a use of
/// BaseEntity.IsDeleted - an Achieved sprint must stay visible to the owner's "all sprints" Backlog
/// view and to the Objective-achieve gate check (see AchieveObjectiveCommandHandler), both of which
/// would silently break under the standard !IsDeleted repository filter convention.
/// </summary>
public class Sprint : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Goal { get; set; }

    /// <summary>Null while Draft - only StartSprintCommand ever sets these, once, moving the
    /// sprint straight to Active. There is no dateless-but-scheduled holding state.</summary>
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public string Status { get; set; } = SprintStatuses.Draft;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? AchievedAt { get; set; }

    /// <summary>Set once by SprintLifecycleJob's overdue sweep so the notification fires exactly
    /// once per sprint instead of every 5-minute tick. Never cleared.</summary>
    public DateTimeOffset? OverdueNotifiedAt { get; set; }
}
