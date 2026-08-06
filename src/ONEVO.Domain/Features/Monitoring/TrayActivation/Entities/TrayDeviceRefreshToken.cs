using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

public class TrayDeviceRefreshToken : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid DeviceRegistrationId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsRevoked { get; set; } = false;
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsValid => !IsRevoked && DateTimeOffset.UtcNow < ExpiresAt;
}
