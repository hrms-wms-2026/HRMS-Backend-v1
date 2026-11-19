using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Planner.Entities;

/// <summary>Day planner (2026-10-26 design, Part B): one task placed in a time slot of an employee's day. It never
/// changes the task's due date, estimate or status.</summary>
public class PlannedTaskBlock : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public Guid TaskId { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
}
