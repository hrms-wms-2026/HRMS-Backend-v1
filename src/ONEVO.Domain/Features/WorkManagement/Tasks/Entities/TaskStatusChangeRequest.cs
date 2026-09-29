using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class TaskStatusChangeRequestStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";

    /// <summary>Closed automatically because another approved change (or a direct edit by a
    /// root-module approver) touched the same statuses or reordered the board first.</summary>
    public const string Outdated = "outdated";
}

/// <summary>
/// A request from a member/owner of a non-root module to change the project's task-status template
/// (the ObjectiveId == null TaskStatus rows). Decided by the project's root (IsDefault) module owner or
/// any of its active members. ChangesJson holds a TaskStatusChangeSet: adds, per-status updates
/// with their "from" snapshot, deletes, and the full intended order - applied atomically on approve.
/// </summary>
public class TaskStatusChangeRequest : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid RequestedByEmployeeId { get; set; }
    public string ChangesJson { get; set; } = "{}";
    public string? Note { get; set; }
    public string Status { get; set; } = TaskStatusChangeRequestStatuses.Pending;
    public Guid? DecidedByEmployeeId { get; set; }
    public string? DecisionComment { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
