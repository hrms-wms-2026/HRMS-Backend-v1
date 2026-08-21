using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Settings.Entities;

/// <summary>
/// Role / position / department scoped monitoring overrides. Null = inherit.
/// Unique on (tenant_id, scope_type, scope_id).
/// </summary>
public class MonitoringPolicyOverride : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>"role" | "position" | "department"</summary>
    public string ScopeType { get; set; } = string.Empty;
    public Guid ScopeId { get; set; }

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

    /// <summary>Role/position/department override. Null = inherit from tenant.</summary>
    public int? IdleThresholdMinutes { get; set; }

    public string OverrideReason { get; set; } = string.Empty;
    public Guid SetById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
