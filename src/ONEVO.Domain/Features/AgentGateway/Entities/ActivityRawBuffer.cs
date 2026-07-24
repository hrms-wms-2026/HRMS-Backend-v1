using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.AgentGateway.Entities;

public class ActivityRawBuffer : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AgentDeviceId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public string PayloadJson { get; set; } = "{}";
}
