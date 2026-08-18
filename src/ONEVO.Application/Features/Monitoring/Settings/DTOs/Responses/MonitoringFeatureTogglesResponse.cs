namespace ONEVO.Application.Features.Monitoring.Settings.DTOs.Responses;

public record MonitoringFeatureTogglesResponse(
    bool ActivityMonitoring,
    bool ApplicationTracking,
    bool DocumentTracking,
    bool CommunicationTracking,
    bool ScreenshotCapture,
    bool AutoScreenshotCapture,
    bool MeetingDetection,
    bool DeviceTracking,
    bool WorkLocationVerification,
    bool IdentityVerification,
    bool Biometric,
    DateTimeOffset? UpdatedAt);
