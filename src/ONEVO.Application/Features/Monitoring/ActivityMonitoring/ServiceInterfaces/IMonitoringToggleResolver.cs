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

    /// <summary>
    /// Resolves the effective idle-inactivity threshold, in minutes, for the given employee -
    /// same employee → role → position → department → tenant → default(5) chain as
    /// <see cref="IsEnabledAsync"/>.
    /// </summary>
    Task<int> GetIdleThresholdMinutesAsync(
        Guid tenantId,
        Guid employeeId,
        CancellationToken ct = default);
}
