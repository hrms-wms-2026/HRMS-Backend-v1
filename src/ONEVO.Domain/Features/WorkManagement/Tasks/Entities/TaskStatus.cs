using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class TaskStatusVisibilities
{
    public const string Public = "public";
    public const string Private = "private";
}

public static class TaskStatusCategories
{
    public const string NotStarted = "not_started";
    public const string Active = "active";
    public const string Done = "done";
}

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
    /// <summary>
    /// Persisted and round-tripped through the API (create/edit/list/reorder/clone) for backward
    /// compatibility, but NOT read by any approval/gating logic anywhere in this codebase
    /// (confirmed via full-history grep as of 2026-10-12 - see WM-01 in
    /// docs/superpowers/plans/next/2026-10-12-pre-uat-audit-wm-sec-findings.md). Real task-status-
    /// move governance lives in MoveTaskStatusCommandHandler; the real approval engine is
    /// WorkApprovalRequest/IWorkApprovalEngine. Do not add new logic that branches on this field
    /// without first confirming the decision with product and removing this comment.
    /// </summary>
    public bool RequiresApproval { get; set; }

    /// <summary>See <see cref="RequiresApproval"/> - same dead-field status.</summary>
    public Guid? ApproverId { get; set; }
    public bool MarksTaskComplete { get; set; }
    public string Visibility { get; set; } = TaskStatusVisibilities.Public;
    public string Category { get; set; } = TaskStatusCategories.NotStarted;
    public string Color { get; set; } = "#94A3B8";
}
