using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class WorkTaskTypes
{
    public const string Task = "task";
    public const string Bug = "bug";
    public const string Story = "story";
    public const string Feature = "feature";
}

public static class WorkTaskPriorities
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Critical = "critical";
}

/// <summary>
/// Core Work Management item. Table name stays "tasks" - the C# class is WorkTask to avoid
/// colliding with System.Threading.Tasks.Task.
/// </summary>
public class WorkTask : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid? ParentTaskId { get; set; }
    public Guid ObjectiveId { get; set; }
    public Guid? SprintId { get; set; }
    public string ShortId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TaskType { get; set; } = WorkTaskTypes.Task;
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
