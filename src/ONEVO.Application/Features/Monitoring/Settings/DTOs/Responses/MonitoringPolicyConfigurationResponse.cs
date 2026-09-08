using ONEVO.Application.Features.Monitoring.Settings.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Settings.DTOs.Responses;

public sealed record MonitoringPolicyOverrideResponse(
    Guid Id,
    string ScopeType,
    Guid ScopeId,
    string TargetName,
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
    string OverrideReason,
    DateTimeOffset UpdatedAt);

public sealed record MonitoringPolicyConfigurationResponse(
    MonitoringFeatureTogglesResponse CompanyDefault,
    IReadOnlyList<MonitoringPolicyOverrideResponse> Overrides,
    bool HasActiveCompanyContext);
