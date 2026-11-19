using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Alerts;

public static class AttendanceAlertDeductionStatuses
{
    /// <summary>Owed, but the employee has no entitlement row for that type and year yet; charged once it exists.</summary>
    public const string Pending = "pending";
    public const string Charged = "charged";
    public const string Refunded = "refunded";
}

/// <summary>
/// The leave a late clock-in / early clock-out alert costs under the clock-in policy: minutes × multiplier, charged to
/// one time-off type's entitlement for the work date's year. One row per alert, so re-runs stay idempotent and refunds
/// reverse exactly what was charged. The multiplier is frozen at the rule that first charged it.
/// </summary>
public class AttendanceAlertDeduction : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public Guid AlertId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public int Year { get; set; }
    public int Minutes { get; set; }
    public decimal Multiplier { get; set; }
    public decimal Hours { get; set; }
    public string Status { get; set; } = AttendanceAlertDeductionStatuses.Pending;
    public DateTimeOffset? ChargedAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public Guid? ChargeAuditId { get; set; }
    public Guid? RefundAuditId { get; set; }
}
