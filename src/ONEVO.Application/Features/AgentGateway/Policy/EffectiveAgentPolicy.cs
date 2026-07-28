using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.AgentGateway.Policy;

/// <summary>
/// Normalized, runtime-gated policy returned to an enrolled desktop agent.
/// Raw tenant configuration is never sent directly to the agent.
/// </summary>
public sealed record EffectiveAgentPolicy(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("activity_monitoring")] bool ActivityMonitoring,
    [property: JsonPropertyName("application_tracking")] bool ApplicationTracking,
    [property: JsonPropertyName("meeting_detection")] bool MeetingDetection,
    [property: JsonPropertyName("screenshot_capture")] bool ScreenshotCapture,
    [property: JsonPropertyName("snapshot_interval_seconds")] int SnapshotIntervalSeconds,
    [property: JsonPropertyName("app_sample_interval_seconds")] int AppSampleIntervalSeconds,
    [property: JsonPropertyName("idle_threshold_seconds")] int IdleThresholdSeconds,
    [property: JsonPropertyName("screenshot_consent_timeout_seconds")] int ScreenshotConsentTimeoutSeconds,
    [property: JsonPropertyName("screenshot_cooldown_seconds")] int ScreenshotCooldownSeconds,
    [property: JsonPropertyName("screenshot_scope")] string ScreenshotScope,
    [property: JsonPropertyName("max_screenshot_bytes")] int MaxScreenshotBytes);

