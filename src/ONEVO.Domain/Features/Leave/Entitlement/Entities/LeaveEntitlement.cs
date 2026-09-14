using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Entitlement.Entities;

public class LeaveEntitlement : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public int Year { get; set; }
    public decimal TotalHours { get; set; }
    public decimal UsedHours { get; set; }
    public decimal PendingHours { get; set; }
    public decimal CarriedForwardHours { get; set; }
    public string Source { get; set; } = Common.LeaveEntitlementSources.Auto;
    public string? ManualReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
