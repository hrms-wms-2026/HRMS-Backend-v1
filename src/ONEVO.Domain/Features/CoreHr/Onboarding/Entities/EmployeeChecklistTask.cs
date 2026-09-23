using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public static class EmployeeChecklistTaskStatuses
{
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    /// <summary>Approved-bypass terminal state - counts as "done" for the offboarding
    /// completion gate (see CompleteOffboardingCommandHandler, Task 15) but is distinct from
    /// Completed for audit and the Track Exit Work progress view.</summary>
    public const string Bypassed = "bypassed";
}

/// <summary>A checklist task instantiated for one employee. IsBypassable/BypassPenaltyDescription/
/// Category are offboarding-only fields (default false/null/null) copied from the owning
/// template's task definition at instantiation - see design spec §4.2.</summary>
public class EmployeeChecklistTask : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? TemplateId { get; set; }
    public string LifecycleType { get; set; } = string.Empty;
    public string TaskTitle { get; set; } = string.Empty;
    public string OwnerType { get; set; } = string.Empty;
    public int? Sequence { get; set; }
    public Guid AssignedToId { get; set; }
    public DateOnly DueDate { get; set; }
    public bool IsRequired { get; set; } = true;
    public bool IsBypassable { get; set; } = false;
    public string? BypassPenaltyDescription { get; set; }
    public string? Category { get; set; }
    public Guid? OffboardingRecordId { get; set; }
    public string Status { get; set; } = EmployeeChecklistTaskStatuses.Pending;
    public DateTimeOffset? CompletedAt { get; set; }
}
