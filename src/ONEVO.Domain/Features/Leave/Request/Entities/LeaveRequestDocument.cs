using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Leave.Request.Entities;

public class LeaveRequestDocument : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LeaveRequestId { get; set; }
    public Guid FileRecordId { get; set; }
}
