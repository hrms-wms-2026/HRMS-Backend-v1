using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.Policy.DTOs;

public sealed record TrayAgentPolicyDto(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("activity_signal_enabled")] bool ActivitySignalEnabled,
    [property: JsonPropertyName("app_usage_enabled")] bool AppUsageEnabled,
    [property: JsonPropertyName("screenshot_enabled")] bool ScreenshotEnabled,
    [property: JsonPropertyName("inactivity_screenshot_enabled")] bool InactivityScreenshotEnabled,
    [property: JsonPropertyName("camera_verification_enabled")] bool CameraVerificationEnabled,
    [property: JsonPropertyName("idle_threshold_minutes")] int IdleThresholdMinutes,
    [property: JsonPropertyName("valid_until")] DateTimeOffset ValidUntil,
    [property: JsonPropertyName("effective_scope")] string EffectiveScope = "employee",
    [property: JsonPropertyName("location_tracking_enabled")] bool LocationTrackingEnabled = false,
    [property: JsonPropertyName("tray_clock_in_enabled")] bool TrayClockInEnabled = false);
