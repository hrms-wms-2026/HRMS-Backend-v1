using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Request.Entities;

public class LeaveRequestApprover : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LeaveRequestId { get; set; }
    public Guid ApproverEmployeeId { get; set; }
    public int SequenceOrder { get; set; }
    public string Status { get; set; } = Common.LeaveRequestApproverStatuses.Pending;
    public string? Comment { get; set; }
    public Guid? DelegatedFromApproverId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
