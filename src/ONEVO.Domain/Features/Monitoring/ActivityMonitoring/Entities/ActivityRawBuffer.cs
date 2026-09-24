using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;

/// <summary>
/// Append-only landing zone for raw Tray App activity payloads.
/// </summary>
public class ActivityRawBuffer : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AgentDeviceId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
}
