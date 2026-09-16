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

    Task<bool> IsEnabledAsync(
        Guid tenantId,
        Guid userId,
        Guid legalEntityId,
        MonitoringCapability capability,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves the effective idle-inactivity threshold, in minutes, for the given employee -
    /// same employee → work mode → role → position → department → tenant → default(2) chain as
    /// <see cref="IsEnabledAsync"/>.
    /// </summary>
    Task<int> GetIdleThresholdMinutesAsync(
        Guid tenantId,
        Guid employeeId,
        CancellationToken ct = default);

    Task<int> GetIdleThresholdMinutesAsync(
        Guid tenantId,
        Guid userId,
        Guid legalEntityId,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves the effective allowed geofence radius, in meters, for the given employee - same
    /// employee → work mode → role → position → department → tenant chain as
    /// <see cref="IsEnabledAsync"/> and <see cref="GetIdleThresholdMinutesAsync"/>. Null when no
    /// tier configures a radius (no proximity restriction).
    /// </summary>
    Task<int?> GetAllowedRadiusMetersAsync(
        Guid tenantId,
        Guid employeeId,
        CancellationToken ct = default);

    Task<int?> GetAllowedRadiusMetersAsync(
        Guid tenantId,
        Guid userId,
        Guid legalEntityId,
        CancellationToken ct = default);
}
