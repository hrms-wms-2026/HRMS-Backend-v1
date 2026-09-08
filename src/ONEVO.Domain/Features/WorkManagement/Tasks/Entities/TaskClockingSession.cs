using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

/// <summary>
/// One Clock-in-to-Push work session on a WorkTask. At most one session per task may be open.
/// </summary>
public class TaskClockingSession : BaseEntity
{
    public Guid TaskId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTimeOffset ClockInAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClockOutAt { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Reason { get; set; }
}
