using ONEVO.Domain.Features.Monitoring.TrayActivation.Enums;

namespace ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

public class TrayDeviceAuthorization
{
    public Guid Id { get; set; }
    public string DeviceCodeHash { get; set; } = string.Empty;
    public string UserCodeHash { get; set; } = string.Empty;
    public string DeviceFingerprintHash { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceOs { get; set; } = string.Empty;
    public string ClientVersion { get; set; } = string.Empty;
    public DeviceAuthorizationStatus Status { get; set; } = DeviceAuthorizationStatus.Pending;
    public Guid? ApprovedTenantId { get; set; }
    public Guid? ApprovedUserId { get; set; }
    public Guid? ApprovedLegalEntityId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? LastPolledAt { get; set; }
    public int PollViolationCount { get; set; }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public bool CanApprove(DateTimeOffset now) => Status == DeviceAuthorizationStatus.Pending && !IsExpired(now);

    public bool CanConsume(DateTimeOffset now) => Status == DeviceAuthorizationStatus.Approved && !IsExpired(now);
}
