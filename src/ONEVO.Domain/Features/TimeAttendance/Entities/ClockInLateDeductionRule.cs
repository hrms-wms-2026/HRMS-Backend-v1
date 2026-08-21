using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.TimeAttendance.Entities;

public class ClockInLateDeductionRule : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ClockInPolicyId { get; set; }
    public int LateArrivalMinute { get; set; }
    public decimal Multiplier { get; set; }
    public Guid TimeOffTypeId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ClockInPolicy? ClockInPolicy { get; set; }
}
