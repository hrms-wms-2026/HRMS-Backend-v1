using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Settings.Entities;

/// <summary>
/// Tenant-level ON/OFF switches for monitoring capabilities.
/// Unique on tenant_id.
/// </summary>
public class MonitoringFeatureToggles : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public bool ActivityMonitoring { get; set; }
    public bool ApplicationTracking { get; set; }
    public bool DocumentTracking { get; set; }
    public bool CommunicationTracking { get; set; }
    public bool ScreenshotCapture { get; set; }
    public bool AutoScreenshotCapture { get; set; }
    public bool MeetingDetection { get; set; }
    public bool DeviceTracking { get; set; }
    public bool WorkLocationVerification { get; set; }
    public bool IdentityVerification { get; set; }
    public bool Biometric { get; set; }

    /// <summary>
    /// Minutes of continuous mouse/keyboard inactivity before the TrayApp shows the
    /// "Activity check" screenshot prompt. Null = tenant has not configured a value yet
    /// (resolver falls back to <c>MonitoringToggleResolution.DefaultIdleThresholdMinutes</c>).
    /// </summary>
    public int? IdleThresholdMinutes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
