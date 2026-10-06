using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class WorkTaskPriorities
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Critical = "critical";
}

public static class WorkTaskKinds
{
    public const string Standard = "standard";
    public const string EmployeeChecklist = "employee_checklist";
}

public static class WorkTaskVisibilityScopes
{
    public const string Module = "module";
    public const string Assignees = "assignees";
}

/// <summary>
/// Core Work Management item. Table name stays "tasks" - the C# class is WorkTask to avoid
/// colliding with System.Threading.Tasks.Task.
/// </summary>
public class WorkTask : BaseEntity
{
    public string TaskKind { get; set; } = WorkTaskKinds.Standard;
    public string VisibilityScope { get; set; } = WorkTaskVisibilityScopes.Module;
    public Guid ProjectId { get; set; }
    public Guid? ParentTaskId { get; set; }
    public Guid ObjectiveId { get; set; }
    /// <summary>The Module whose current owner is this object's creator position - the approver of
    /// edits by anyone below it. Null means "use the default": Module → its parent, Task → its own
    /// Module, Sprint → the project root Module.</summary>
    public Guid? CreatorPositionObjectiveId { get; set; }
    public Guid? SprintId { get; set; }
    public string ShortId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CategoryId { get; set; }
    public Guid StatusId { get; set; }
    public string Priority { get; set; } = WorkTaskPriorities.Medium;
    public int? StoryPoints { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal? EstimatedHours { get; set; }
    public decimal CompletedHours { get; set; }
    public int ProgressPercent { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
