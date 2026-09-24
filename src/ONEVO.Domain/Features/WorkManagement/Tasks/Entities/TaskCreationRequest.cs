using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class TaskCreationRequestStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// A non-owner Objective member's request to create a task, decided by the Objective owner.
/// See docs/superpowers/specs/next/2026-08-16-work-management-task-foundation-design.md §3.3.
/// </summary>
public class TaskCreationRequest : BaseEntity
{
    public Guid ObjectiveId { get; set; }
    public Guid RequestedByEmployeeId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = TaskCreationRequestStatuses.Pending;
    public Guid? DecidedByEmployeeId { get; set; }
    public string? DecisionComment { get; set; }
    public Guid? CreatedTaskId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
