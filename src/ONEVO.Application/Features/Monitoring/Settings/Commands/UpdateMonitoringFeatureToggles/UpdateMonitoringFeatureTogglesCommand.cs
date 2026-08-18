using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Settings.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Settings.Commands.UpdateMonitoringFeatureToggles;

// Full-replace PUT: every capability must be supplied on every call. There is no
// nullable-means-preserve semantics here - the admin settings screen always
// submits the complete current state of all 11 switches.
public record UpdateMonitoringFeatureTogglesCommand(
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
    bool Biometric) : IRequest<Result<MonitoringFeatureTogglesResponse>>;
