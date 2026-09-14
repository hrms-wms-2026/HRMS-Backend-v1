using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Entities;

public sealed class WorkAreaChangeRequest : ITenantOwnedEntity
{
    public const string StatusPending = "pending";
    public const string StatusApproved = "approved";
    public const string StatusRejected = "rejected";
    public const string StatusCancelled = "cancelled";

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LegalEntityId { get; set; }
    public DateOnly Date { get; set; }

    public Guid? CurrentWorkModeId { get; set; }
    public string CurrentWorkModeName { get; set; } = string.Empty;

    // Nullable only to accommodate pre-migration rows whose original "field" value has no
    // WorkMode counterpart to remap to (see Task 14) - every row created by CreateAsync from
    // here on always populates this; the workflow never persists a request with a null target.
    public Guid? RequestedWorkModeId { get; set; }
    public string RequestedWorkModeName { get; set; } = string.Empty;

    // Read-only historical label for pre-migration rows whose original string value ("field")
    // has no 1:1 WorkMode counterpart - see spec Data Migration step 4 ("either" maps cleanly
    // onto the migrated Hybrid row and needs no legacy label). Null for every row created after
    // this feature ships.
    public string? LegacyWorkAreaLabel { get; set; }

    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = StatusPending;
    public DateTimeOffset RequestedAt { get; set; }
    public Guid? ReviewedById { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
}
