using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Entities;

public sealed class AttendanceCorrection : ITenantOwnedEntity
{
    public const string TypeClockIn = "clock_in";
    public const string TypeClockOut = "clock_out";
    public const string TypeBreak = "break";
    public const string TypeFullDay = "full_day";

    public const string StatusPending = "pending";
    public const string StatusApproved = "approved";
    public const string StatusRejected = "rejected";
    public const string StatusCancelled = "cancelled";

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LegalEntityId { get; set; }
    public Guid? PresenceSessionId { get; set; }
    public Guid? AttendanceRecordId { get; set; }
    // The Phase 1 inventory omitted work_date, but a break-only correction has no timestamp
    // from which to reconstruct it during approval/list reads. This explicit value preserves the
    // request target and is documented as an inventory-gap extension in the backend report.
    public DateOnly WorkDate { get; set; }
    public string CorrectionType { get; set; } = string.Empty;
    public DateTimeOffset? OriginalClockInAt { get; set; }
    public DateTimeOffset? OriginalClockOutAt { get; set; }
    public DateTimeOffset? RequestedClockInAt { get; set; }
    public DateTimeOffset? RequestedClockOutAt { get; set; }
    public string? OriginalBreakJson { get; set; }
    public string? RequestedBreakJson { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string Status { get; set; } = StatusPending;
    public bool ApprovalRequired { get; set; }
    public Guid RequestedById { get; set; }
    public Guid? ReviewedById { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record AttendanceCorrectionBreak(
    DateTimeOffset BreakStart,
    DateTimeOffset BreakEnd,
    string BreakType);
