using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Settings.Entities;

/// <summary>
/// Per-employee monitoring capability overrides. Null = inherit from policy/tenant.
/// Unique on (tenant_id, employee_id).
/// </summary>
public class EmployeeMonitoringOverride : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public bool? ActivityMonitoring { get; set; }
    public bool? ApplicationTracking { get; set; }
    public bool? DocumentTracking { get; set; }
    public bool? CommunicationTracking { get; set; }
    public bool? ScreenshotCapture { get; set; }
    public bool? AutoScreenshotCapture { get; set; }
    public bool? MeetingDetection { get; set; }
    public bool? DeviceTracking { get; set; }
    public bool? WorkLocationVerification { get; set; }
    public bool? IdentityVerification { get; set; }
    public bool? Biometric { get; set; }

    /// <summary>Per-employee override. Null = inherit from role/position/department/tenant.</summary>
    public int? IdleThresholdMinutes { get; set; }

    /// <summary>
    /// Maximum radius in meters from the configured work location. Null = inherit from
    /// work mode/role/position/department/tenant. Used by WorkLocationVerification to validate
    /// clock-in proximity.
    /// </summary>
    public int? AllowedRadiusMeters { get; set; }

    public string OverrideReason { get; set; } = string.Empty;
    public Guid SetById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
