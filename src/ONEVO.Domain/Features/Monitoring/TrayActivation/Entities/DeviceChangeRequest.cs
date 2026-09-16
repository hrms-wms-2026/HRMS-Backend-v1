using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

public sealed class DeviceChangeRequest : ITenantOwnedEntity
{
    public const string StatusPending = "pending";
    public const string StatusApproved = "approved";
    public const string StatusRejected = "rejected";

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? LegalEntityId { get; set; }
    public Guid? CurrentDeviceRegistrationId { get; set; }
    public string NewDeviceFingerprint { get; set; } = string.Empty;
    public string NewDeviceName { get; set; } = string.Empty;
    public string NewDeviceOs { get; set; } = string.Empty;
    public string Status { get; set; } = StatusPending;
    public DateTimeOffset RequestedAt { get; set; }
    public Guid? ReviewedById { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
}
