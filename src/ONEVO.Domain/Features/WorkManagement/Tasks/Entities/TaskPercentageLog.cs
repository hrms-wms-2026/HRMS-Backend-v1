using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class TaskPercentageLogSources
{
    public const string Push = "push";
    public const string ManualEdit = "manual_edit";
    public const string StatusChange = "status_change";
}

/// <summary>Audit row for every change to WorkTask.ProgressPercent.</summary>
public class TaskPercentageLog : BaseEntity
{
    public Guid TaskId { get; set; }
    public Guid EmployeeId { get; set; }
    public int PreviousPercent { get; set; }
    public int NewPercent { get; set; }
    public string Source { get; set; } = TaskPercentageLogSources.ManualEdit;
    public Guid? ClockingSessionId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}
