using ONEVO.Application.Features.Monitoring.Settings.DTOs.Responses;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;

namespace ONEVO.Application.Features.Monitoring.Settings.Mappers;

public static class MonitoringFeatureTogglesMapper
{
    // Mirrors MonitoringToggleResolution.DefaultIdleThresholdMinutes (Infrastructure) by value,
    // not by reference: Application must not reference Infrastructure (layer dependency rule),
    // and the existing bool defaults just above/below (all-false) are likewise inlined literals
    // rather than calls into MonitoringToggleResolution.Resolve(null,null,null,null,null) - same
    // precedent, same trade-off.
    private const int DefaultIdleThresholdMinutes = 15;

    /// <summary>
    /// Null entity (no row yet) maps to all-false defaults, IdleThresholdMinutes = the
    /// resolver's default (2), and UpdatedAt = null, mirroring
    /// MonitoringToggleResolverService's own null-row-means-default semantics.
    /// </summary>
    public static MonitoringFeatureTogglesResponse ToResponse(MonitoringSetting? entity) =>
        entity is null
            ? new MonitoringFeatureTogglesResponse(
                false, false, false, false, false, false, false, false, false, false, false,
                DefaultIdleThresholdMinutes, null, null)
            : new MonitoringFeatureTogglesResponse(
                // A base row sets every switch (check constraint); "?? false" only satisfies the type.
                entity.ActivityMonitoring ?? false,
                entity.ApplicationTracking ?? false,
                entity.DocumentTracking ?? false,
                entity.CommunicationTracking ?? false,
                entity.ScreenshotCapture ?? false,
                entity.AutoScreenshotCapture ?? false,
                entity.MeetingDetection ?? false,
                entity.DeviceTracking ?? false,
                entity.WorkLocationVerification ?? false,
                entity.IdentityVerification ?? false,
                entity.Biometric ?? false,
                entity.IdleThresholdMinutes ?? DefaultIdleThresholdMinutes,
                entity.AllowedRadiusMeters,
                entity.UpdatedAt);
}
