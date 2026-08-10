using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.Policy.DTOs;

/// <summary>
/// Effective monitoring policy for the calling tray device's employee/tenant.
/// Polled by the Agent Service to learn which monitoring capabilities are
/// currently enabled. <see cref="Version"/> is a stable fingerprint of the
/// underlying toggle values so callers can cheaply detect changes.
/// </summary>
public sealed record TrayAgentPolicyDto(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("activity_signal_enabled")] bool ActivitySignalEnabled,
    [property: JsonPropertyName("app_usage_enabled")] bool AppUsageEnabled,
    [property: JsonPropertyName("screenshot_enabled")] bool ScreenshotEnabled,
    [property: JsonPropertyName("inactivity_screenshot_enabled")] bool InactivityScreenshotEnabled,
    [property: JsonPropertyName("camera_verification_enabled")] bool CameraVerificationEnabled,
    [property: JsonPropertyName("valid_until")] DateTimeOffset ValidUntil);
