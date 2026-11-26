using MediatR;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Settings.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Settings.Commands.UpdateMonitoringFeatureToggles;

// Full-replace PUT: every capability must be supplied on every call. There is no
// nullable-means-preserve semantics here - the admin settings screen always
// submits the complete current state.
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
    bool Biometric,
    int IdleThresholdMinutes,
    int? AllowedRadiusMeters)
    : IRequest<Result<MonitoringFeatureTogglesResponse>>, IAuditedCommand<Result<MonitoringFeatureTogglesResponse>>
{
    // The toggles belong to the active company of the tenant. Before/after values (which toggles
    // changed) come from MonitoringFeatureTogglesAuditStateReader.
    public AuditEntry DescribeAudit(Result<MonitoringFeatureTogglesResponse>? response)
        => AuditEntry.For("monitoring.toggles_updated", "tenant", targetId: null);
}
