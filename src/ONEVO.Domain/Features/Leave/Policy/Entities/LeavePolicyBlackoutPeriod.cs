using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Policy.Entities;

public class LeavePolicyBlackoutPeriod : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LeavePolicyId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Reason { get; set; }
}
