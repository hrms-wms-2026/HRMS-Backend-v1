using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

public static class WorkApprovalRequestStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    /// <summary>The target was deleted or changed after the request was made - nothing was applied.</summary>
    public const string Stale = "stale";
}

/// <summary>How the approver was resolved. Hierarchy approvals follow the position (anyone at or
/// above it may decide, so a transfer moves the right); Hr approvals are fixed to the resolved
/// HR approver because they sit outside the project tree.</summary>
public static class WorkApprovalSources
{
    public const string Hierarchy = "hierarchy";
    public const string Hr = "hr";
}

public static class WorkTargetTypes
{
    public const string Module = "module";
    public const string Task = "task";
    public const string Sprint = "sprint";
    /// <summary>Project-wide settings with no single row as target, e.g. the task-status template.</summary>
    public const string Project = "project";
}

public static class WorkActionTypes
{
    public const string TaskCreate = "task.create";
    public const string TaskEdit = "task.edit";
    public const string TaskDelete = "task.delete";
    public const string TaskStatusChange = "task.status_change";
    public const string ModuleEdit = "module.edit";
    public const string ModuleDelete = "module.delete";
    public const string ModuleTransfer = "module.transfer";
    public const string ModuleAchieve = "module.achieve";
    public const string ModuleUnachieve = "module.unachieve";
    public const string ModuleAllocationExtend = "module.allocation_extend";
    public const string SprintCreate = "sprint.create";
    public const string SprintEdit = "sprint.edit";
    public const string SprintDelete = "sprint.delete";
    public const string SprintStart = "sprint.start";
    public const string SprintComplete = "sprint.complete";
    public const string SprintAchieve = "sprint.achieve";
    /// <summary>Add/rename/delete/reorder the project's task statuses. Not task.status_change (one task's move).</summary>
    public const string ProjectStatusTemplateChange = "project.status_template_change";
}

/// <summary>
/// One pending-or-decided approval for any Work Management action, replacing the per-type request
/// tables. The approver is resolved from the target's creator position in the project Module tree
/// (see the 2026-09-28 hierarchy/approval/notification engine spec).
/// </summary>
public class WorkApprovalRequest : BaseEntity
{
    public Guid ProjectId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    /// <summary>Null for create actions.</summary>
    public Guid? TargetId { get; set; }
    public string TargetTitle { get; set; } = string.Empty;
    /// <summary>The Module whose current owner holds the approving position. Null only for Hr approvals.</summary>
    public Guid? PositionObjectiveId { get; set; }
    public string ApproverSource { get; set; } = WorkApprovalSources.Hierarchy;
    /// <summary>The approver resolved at submit time - who was notified. Hierarchy approvals are re-checked at decision time.</summary>
    public Guid ApproverEmployeeId { get; set; }
    public Guid RequestedByEmployeeId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = WorkApprovalRequestStatuses.Pending;
    public Guid? DecidedByEmployeeId { get; set; }
    public string? DecisionComment { get; set; }
    /// <summary>Target's UpdatedAt when the request was made - appliers compare it to detect stale requests.</summary>
    public DateTimeOffset? TargetUpdatedAtSnapshot { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
