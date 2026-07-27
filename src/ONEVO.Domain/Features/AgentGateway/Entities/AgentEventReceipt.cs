namespace ONEVO.Domain.Features.AgentGateway.Entities;

public class AgentEventReceipt
{
    public Guid EventId { get; set; }
    public Guid AgentDeviceId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
}
