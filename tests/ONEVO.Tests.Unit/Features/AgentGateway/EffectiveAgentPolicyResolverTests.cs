using System.Text.Json;
using ONEVO.Application.Features.AgentGateway.Policy;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public sealed class EffectiveAgentPolicyResolverTests
{
    private readonly EffectiveAgentPolicyResolver _resolver = new();

    [Fact]
    public void Resolve_ValidPolicyAndAllRuntimeGates_EnablesConfiguredMonitoring()
    {
        const string rawPolicy =
            """
            {
              "version": 7,
              "activity_monitoring": true,
              "application_tracking": true,
              "meeting_detection": true,
              "screenshot_capture": true,
              "snapshot_interval_seconds": 30,
              "app_sample_interval_seconds": 3,
              "idle_threshold_seconds": 600,
              "screenshot_consent_timeout_seconds": 25,
              "screenshot_cooldown_seconds": 1200,
              "screenshot_scope": "active_monitor",
              "max_screenshot_bytes": 1048576
            }
            """;

        var result = _resolver.Resolve(rawPolicy, new EffectiveAgentPolicyContext(
            DeviceApproved: true,
            ActiveAgentSession: true,
            ActivePresence: true,
            MonitoringDisclosureAccepted: true));

        Assert.Equal(7, result.Version);
        Assert.True(result.ActivityMonitoring);
        Assert.True(result.ApplicationTracking);
        Assert.True(result.MeetingDetection);
        Assert.True(result.ScreenshotCapture);
        Assert.Equal(30, result.SnapshotIntervalSeconds);
        Assert.Equal(3, result.AppSampleIntervalSeconds);
        Assert.Equal(600, result.IdleThresholdSeconds);
        Assert.Equal(25, result.ScreenshotConsentTimeoutSeconds);
        Assert.Equal(1200, result.ScreenshotCooldownSeconds);
        Assert.Equal("active_monitor", result.ScreenshotScope);
        Assert.Equal(1048576, result.MaxScreenshotBytes);
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void Resolve_MissingRuntimeGate_DisablesAllMonitoring(
        bool deviceApproved,
        bool activeAgentSession,
        bool activePresence,
        bool monitoringDisclosureAccepted)
    {
        const string rawPolicy =
            """
            {
              "activity_monitoring": true,
              "application_tracking": true,
              "meeting_detection": true,
              "screenshot_capture": true
            }
            """;

        var result = _resolver.Resolve(rawPolicy, new EffectiveAgentPolicyContext(
            deviceApproved,
            activeAgentSession,
            activePresence,
            monitoringDisclosureAccepted));

        Assert.False(result.ActivityMonitoring);
        Assert.False(result.ApplicationTracking);
        Assert.False(result.MeetingDetection);
        Assert.False(result.ScreenshotCapture);
    }

    [Fact]
    public void Resolve_UnknownScreenshotScope_DisablesScreenshotCapture()
    {
        const string rawPolicy =
            """
            {
              "activity_monitoring": true,
              "screenshot_capture": true,
              "screenshot_scope": "all_monitors"
            }
            """;

        var result = _resolver.Resolve(rawPolicy, AllGatesOpen());

        Assert.True(result.ActivityMonitoring);
        Assert.False(result.ScreenshotCapture);
        Assert.Equal("active_monitor", result.ScreenshotScope);
    }

    [Fact]
    public void Resolve_InvalidJson_ReturnsSafeDisabledDefaults()
    {
        var result = _resolver.Resolve("{not-json", AllGatesOpen());

        Assert.Equal(1, result.Version);
        Assert.False(result.ActivityMonitoring);
        Assert.False(result.ApplicationTracking);
        Assert.False(result.MeetingDetection);
        Assert.False(result.ScreenshotCapture);
        Assert.Equal(60, result.SnapshotIntervalSeconds);
        Assert.Equal(5, result.AppSampleIntervalSeconds);
        Assert.Equal(900, result.IdleThresholdSeconds);
        Assert.Equal(30, result.ScreenshotConsentTimeoutSeconds);
        Assert.Equal(900, result.ScreenshotCooldownSeconds);
        Assert.Equal("active_monitor", result.ScreenshotScope);
        Assert.Equal(2097152, result.MaxScreenshotBytes);
    }

    [Fact]
    public void Resolve_OutOfRangeNumbers_ClampsToSupportedBounds()
    {
        const string rawPolicy =
            """
            {
              "activity_monitoring": true,
              "snapshot_interval_seconds": 1,
              "app_sample_interval_seconds": 1000,
              "idle_threshold_seconds": 5,
              "screenshot_consent_timeout_seconds": 999,
              "screenshot_cooldown_seconds": 1,
              "max_screenshot_bytes": 99999999
            }
            """;

        var result = _resolver.Resolve(rawPolicy, AllGatesOpen());

        Assert.Equal(15, result.SnapshotIntervalSeconds);
        Assert.Equal(60, result.AppSampleIntervalSeconds);
        Assert.Equal(60, result.IdleThresholdSeconds);
        Assert.Equal(120, result.ScreenshotConsentTimeoutSeconds);
        Assert.Equal(60, result.ScreenshotCooldownSeconds);
        Assert.Equal(5242880, result.MaxScreenshotBytes);
    }

    [Fact]
    public void EffectivePolicy_SerializesUsingStableSnakeCaseContract()
    {
        var policy = _resolver.Resolve("{}", AllGatesOpen());

        var json = JsonSerializer.Serialize(policy);

        Assert.Contains("\"activity_monitoring\"", json, StringComparison.Ordinal);
        Assert.Contains("\"screenshot_consent_timeout_seconds\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivityMonitoring", json, StringComparison.Ordinal);
    }

    private static EffectiveAgentPolicyContext AllGatesOpen() => new(
        DeviceApproved: true,
        ActiveAgentSession: true,
        ActivePresence: true,
        MonitoringDisclosureAccepted: true);
}
