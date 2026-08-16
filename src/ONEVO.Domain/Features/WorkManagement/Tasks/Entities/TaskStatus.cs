using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

/// <summary>
/// Configurable task-status definitions. A row with ObjectiveId == null is a Project-level
/// template; a row with ObjectiveId set is that Objective's own independently-customizable copy.
/// See docs/superpowers/project_ core/phase1-table-inventory.md "task_statuses".
/// </summary>
public class TaskStatus : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid? ObjectiveId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool RequiresApproval { get; set; }
    public Guid? ApproverId { get; set; }
    public bool MarksTaskComplete { get; set; }
}
