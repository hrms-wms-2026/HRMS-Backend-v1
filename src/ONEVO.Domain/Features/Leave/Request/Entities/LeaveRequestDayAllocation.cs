using ONEVO.Domain.Common;
using ONEVO.Domain.Features.Leave.Common;

namespace ONEVO.Domain.Features.Leave.Request.Entities;

public class LeaveRequestDayAllocation : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LeaveRequestId { get; set; }
    public DateOnly LeaveDate { get; set; }
    public decimal HoursUnit { get; set; }
    public decimal PaidHoursUnit { get; set; }
    public decimal UnpaidHoursUnit { get; set; }
    public string Status { get; set; } = LeaveRequestDayAllocationStatuses.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CancelledAt { get; set; }
}
