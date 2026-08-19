using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public static class BypassRequestStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
}

/// <summary>Mirrors task_approvals' shape (single named approver, one pending request per
/// subject row) per the 2026-08-17 offboarding-execution design's explicit instruction to follow
/// that pattern without touching Work Management tables. No notification-row FK - the
/// notifications table doesn't exist anywhere in this codebase (see design spec §2).</summary>
public class OffboardingTaskBypassRequest : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeChecklistTaskId { get; set; }
    public Guid OffboardingRecordId { get; set; }
    public Guid RequestedById { get; set; }
    public Guid ApproverId { get; set; }
    public string BypassReason { get; set; } = string.Empty;
    public string? PenaltyDescription { get; set; }
    /// <summary>The task's Status at the moment this request was created (pending/in_progress) -
    /// restored onto the task by RejectBypassRequestCommandHandler (Task 15) so rejection returns
    /// the task to exactly where it was, not an assumed default.</summary>
    public string PriorTaskStatus { get; set; } = string.Empty;
    public string Status { get; set; } = BypassRequestStatuses.Pending;
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionComment { get; set; }
}
