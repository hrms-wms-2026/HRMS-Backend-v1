using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

public static class SprintActivityActions
{
    public const string Created = "created";
    public const string Edited = "edited";
    public const string Started = "started";
    public const string Completed = "completed";
    public const string Achieved = "achieved";
    public const string TasksAdded = "tasks_added";
    public const string TasksRemoved = "tasks_removed";
}

/// <summary>Audit row for every sprint action (spec D6). Who/when/what - never updated or deleted.</summary>
public class SprintActivityLog : BaseEntity
{
    public Guid SprintId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? DetailsJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
