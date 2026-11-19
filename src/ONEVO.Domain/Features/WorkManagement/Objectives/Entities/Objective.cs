using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

public class Objective : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid? ParentObjectiveId { get; set; }
    public bool IsDefault { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid OwnerId { get; set; }
    public Guid? ReportingManagerId { get; set; }
    /// <summary>The Module whose current owner is this object's creator position - the approver of
    /// edits by anyone below it. Null means "use the default": Module → its parent, Task → its own
    /// Module, Sprint → the project root Module.</summary>
    public Guid? CreatorPositionObjectiveId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal Progress { get; set; }
    public decimal? ActualHours { get; set; }
    public decimal AllocatedHours { get; set; }
    public decimal CompletedHours { get; set; }
    public bool IsAchieved { get; set; }
    public DateTimeOffset? AchievedAt { get; set; }
}
