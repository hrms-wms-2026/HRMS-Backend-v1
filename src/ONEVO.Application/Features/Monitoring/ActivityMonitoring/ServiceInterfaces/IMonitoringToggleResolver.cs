namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;

public enum MonitoringCapability
{
    ActivityMonitoring,
    ApplicationTracking,
    DocumentTracking,
    CommunicationTracking,
    ScreenshotCapture,
    AutoScreenshotCapture,
    MeetingDetection,
    DeviceTracking,
    WorkLocationVerification,
    IdentityVerification,
    Biometric
}

public interface IMonitoringToggleResolver
{
    Task<bool> IsEnabledAsync(
        Guid tenantId,
        Guid employeeId,
        MonitoringCapability capability,
        CancellationToken ct = default);
}
