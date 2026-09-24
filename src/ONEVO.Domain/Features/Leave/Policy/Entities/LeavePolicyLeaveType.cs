using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Policy.Entities;

public class LeavePolicyLeaveType : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LeavePolicyId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public decimal AnnualEntitlementDays { get; set; }
    public decimal? CarryForwardMaxDays { get; set; }
    public int? CarryForwardExpiryMonths { get; set; }
}
