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
    private const int DefaultIdleThresholdMinutes = 2;

    /// <summary>
    /// Null entity (no row yet) maps to all-false defaults, IdleThresholdMinutes = the
    /// resolver's default (2), and UpdatedAt = null, mirroring
    /// MonitoringToggleResolverService's own null-row-means-default semantics.
    /// </summary>
    public static MonitoringFeatureTogglesResponse ToResponse(MonitoringFeatureToggles? entity) =>
        entity is null
            ? new MonitoringFeatureTogglesResponse(
                false, false, false, false, false, false, false, false, false, false, false,
                DefaultIdleThresholdMinutes, null)
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
                entity.IdleThresholdMinutes ?? DefaultIdleThresholdMinutes,
                entity.UpdatedAt);
}
