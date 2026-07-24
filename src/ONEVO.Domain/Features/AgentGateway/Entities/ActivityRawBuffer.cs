using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.AgentGateway.Entities;

public class ActivityRawBuffer : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AgentId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public string EventsJson { get; set; } = "[]";
}
