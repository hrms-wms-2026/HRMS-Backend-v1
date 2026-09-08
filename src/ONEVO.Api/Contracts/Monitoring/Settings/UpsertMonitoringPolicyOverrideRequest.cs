namespace ONEVO.Api.Contracts.Monitoring.Settings;

public sealed record UpsertMonitoringPolicyOverrideRequest(
    bool? ActivityMonitoring,
    bool? ApplicationTracking,
    bool? DocumentTracking,
    bool? CommunicationTracking,
    bool? ScreenshotCapture,
    bool? AutoScreenshotCapture,
    bool? MeetingDetection,
    bool? DeviceTracking,
    bool? WorkLocationVerification,
    bool? IdentityVerification,
    bool? Biometric,
    int? IdleThresholdMinutes,
    string? OverrideReason);
