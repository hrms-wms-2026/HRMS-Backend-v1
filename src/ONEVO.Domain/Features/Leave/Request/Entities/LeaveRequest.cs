using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Request.Entities;

public class LeaveRequest : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? HalfDayPeriod { get; set; }
    public decimal TotalDays { get; set; }
    public decimal PaidDays { get; set; }
    public decimal UnpaidDays { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = Common.LeaveRequestStatuses.Pending;
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ConflictSnapshotJson { get; set; }
    public bool NoticePeriodMissed { get; set; }
    public Guid? SubmittedOnBehalfOfBy { get; set; }
    public string? CancellationReason { get; set; }
    public DateOnly? PartialCancelEffectiveDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
