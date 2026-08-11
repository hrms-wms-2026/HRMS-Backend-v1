using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.DeviceState.Entities;

/// <summary>
/// Device idle/active state sample (seconds since last keyboard/mouse input).
/// </summary>
public class DeviceStateSnapshot : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid AgentDeviceId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public int IdleSeconds { get; set; }
    public bool IsIdle { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
