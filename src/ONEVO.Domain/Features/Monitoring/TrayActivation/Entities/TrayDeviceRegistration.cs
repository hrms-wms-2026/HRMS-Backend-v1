using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

public class TrayDeviceRegistration : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceOs { get; set; } = string.Empty;
    public string DeviceFingerprint { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset ActivatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
