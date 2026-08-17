using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

public static class TaskEditRequestStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// A non-owner Objective member's request to edit an existing task, decided by the task's Objective
/// owner. Structural mirror of TaskCreationRequest - see that entity's doc comment for the design
/// rationale, which applies identically here.
/// </summary>
public class TaskEditRequest : BaseEntity
{
    public Guid TaskId { get; set; }
    public Guid RequestedByEmployeeId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = TaskEditRequestStatuses.Pending;
    public Guid? DecidedByEmployeeId { get; set; }
    public string? DecisionComment { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
