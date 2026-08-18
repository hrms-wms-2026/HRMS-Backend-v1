using ONEVO.Application.Features.Monitoring.Settings.DTOs.Responses;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;

namespace ONEVO.Application.Features.Monitoring.Settings.Mappers;

public static class MonitoringFeatureTogglesMapper
{
    /// <summary>
    /// Null entity (no row yet) maps to all-false defaults with UpdatedAt = null,
    /// mirroring MonitoringToggleResolverService's own null-row-means-false semantics.
    /// </summary>
    public static MonitoringFeatureTogglesResponse ToResponse(MonitoringFeatureToggles? entity) =>
        entity is null
            ? new MonitoringFeatureTogglesResponse(
                false, false, false, false, false, false, false, false, false, false, false, null)
            : new MonitoringFeatureTogglesResponse(
                entity.ActivityMonitoring,
                entity.ApplicationTracking,
                entity.DocumentTracking,
                entity.CommunicationTracking,
                entity.ScreenshotCapture,
                entity.AutoScreenshotCapture,
                entity.MeetingDetection,
                entity.DeviceTracking,
                entity.WorkLocationVerification,
                entity.IdentityVerification,
                entity.Biometric,
                entity.UpdatedAt);
}
