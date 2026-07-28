using System.Text.Json;

namespace ONEVO.Application.Features.AgentGateway.Policy;

public sealed class EffectiveAgentPolicyResolver : IEffectiveAgentPolicyResolver
{
    private const int DefaultSnapshotIntervalSeconds = 60;
    private const int DefaultAppSampleIntervalSeconds = 5;
    private const int DefaultIdleThresholdSeconds = 900;
    private const int DefaultScreenshotConsentTimeoutSeconds = 30;
    private const int DefaultScreenshotCooldownSeconds = 900;
    private const int DefaultMaxScreenshotBytes = 2 * 1024 * 1024;
    private const string SupportedScreenshotScope = "active_monitor";

    public EffectiveAgentPolicy Resolve(
        string? rawPolicyJson,
        EffectiveAgentPolicyContext context)
    {
        if (string.IsNullOrWhiteSpace(rawPolicyJson))
            return DisabledDefaults();

        try
        {
            using var document = JsonDocument.Parse(rawPolicyJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return DisabledDefaults();

            var root = document.RootElement;
            var runtimeGateOpen =
                context.DeviceApproved &&
                context.ActiveAgentSession &&
                context.ActivePresence &&
                context.MonitoringDisclosureAccepted;

            var activityMonitoring =
                runtimeGateOpen &&
                ReadBoolean(root, "activity_monitoring");
            var applicationTracking =
                activityMonitoring &&
                ReadBoolean(root, "application_tracking");
            var meetingDetection =
                applicationTracking &&
                ReadBoolean(root, "meeting_detection");

            var configuredScope = ReadString(root, "screenshot_scope");
            var scopeSupported =
                string.IsNullOrWhiteSpace(configuredScope) ||
                string.Equals(
                    configuredScope,
                    SupportedScreenshotScope,
                    StringComparison.Ordinal);

            return new EffectiveAgentPolicy(
                Version: Math.Max(1, ReadInteger(root, "version", 1)),
                ActivityMonitoring: activityMonitoring,
                ApplicationTracking: applicationTracking,
                MeetingDetection: meetingDetection,
                ScreenshotCapture:
                    activityMonitoring &&
                    scopeSupported &&
                    ReadBoolean(root, "screenshot_capture"),
                SnapshotIntervalSeconds: Clamp(
                    ReadInteger(
                        root,
                        "snapshot_interval_seconds",
                        DefaultSnapshotIntervalSeconds),
                    15,
                    300),
                AppSampleIntervalSeconds: Clamp(
                    ReadInteger(
                        root,
                        "app_sample_interval_seconds",
                        DefaultAppSampleIntervalSeconds),
                    2,
                    60),
                IdleThresholdSeconds: Clamp(
                    ReadInteger(
                        root,
                        "idle_threshold_seconds",
                        DefaultIdleThresholdSeconds),
                    60,
                    7200),
                ScreenshotConsentTimeoutSeconds: Clamp(
                    ReadInteger(
                        root,
                        "screenshot_consent_timeout_seconds",
                        DefaultScreenshotConsentTimeoutSeconds),
                    10,
                    120),
                ScreenshotCooldownSeconds: Clamp(
                    ReadInteger(
                        root,
                        "screenshot_cooldown_seconds",
                        DefaultScreenshotCooldownSeconds),
                    60,
                    86400),
                ScreenshotScope: SupportedScreenshotScope,
                MaxScreenshotBytes: Clamp(
                    ReadInteger(
                        root,
                        "max_screenshot_bytes",
                        DefaultMaxScreenshotBytes),
                    64 * 1024,
                    5 * 1024 * 1024));
        }
        catch (JsonException)
        {
            return DisabledDefaults();
        }
    }

    private static EffectiveAgentPolicy DisabledDefaults() => new(
        Version: 1,
        ActivityMonitoring: false,
        ApplicationTracking: false,
        MeetingDetection: false,
        ScreenshotCapture: false,
        SnapshotIntervalSeconds: DefaultSnapshotIntervalSeconds,
        AppSampleIntervalSeconds: DefaultAppSampleIntervalSeconds,
        IdleThresholdSeconds: DefaultIdleThresholdSeconds,
        ScreenshotConsentTimeoutSeconds: DefaultScreenshotConsentTimeoutSeconds,
        ScreenshotCooldownSeconds: DefaultScreenshotCooldownSeconds,
        ScreenshotScope: SupportedScreenshotScope,
        MaxScreenshotBytes: DefaultMaxScreenshotBytes);

    private static bool ReadBoolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True;

    private static int ReadInteger(
        JsonElement root,
        string name,
        int defaultValue) =>
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var parsed)
            ? parsed
            : defaultValue;

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int Clamp(int value, int minimum, int maximum) =>
        Math.Min(maximum, Math.Max(minimum, value));
}

