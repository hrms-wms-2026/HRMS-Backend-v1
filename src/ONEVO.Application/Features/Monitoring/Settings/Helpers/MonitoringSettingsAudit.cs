using System.Text.Json;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;

namespace ONEVO.Application.Features.Monitoring.Settings.Helpers;

/// <summary>
/// Audit-log entries for monitoring settings changes (company defaults and scoped overrides), so
/// "who switched activity monitoring off, and when" is answerable from audit_logs. Old/new values
/// are JSON snapshots of the toggle fields only.
/// </summary>
public static class MonitoringSettingsAudit
{
    public const string CompanySettingsUpdated = "monitoring.company_settings_updated";
    public const string OverrideSaved = "monitoring.override_saved";
    public const string OverrideRemoved = "monitoring.override_removed";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Snapshot(MonitoringFeatureToggles t) => JsonSerializer.Serialize(new
    {
        t.LegalEntityId,
        t.ActivityMonitoring,
        t.ApplicationTracking,
        t.DocumentTracking,
        t.CommunicationTracking,
        t.ScreenshotCapture,
        t.AutoScreenshotCapture,
        t.MeetingDetection,
        t.DeviceTracking,
        t.WorkLocationVerification,
        t.IdentityVerification,
        t.Biometric,
        t.IdleThresholdMinutes,
        t.AllowedRadiusMeters
    }, JsonOptions);

    public static string Snapshot(MonitoringPolicyOverride o) => JsonSerializer.Serialize(new
    {
        o.ScopeType,
        o.ScopeId,
        o.ActivityMonitoring,
        o.ApplicationTracking,
        o.DocumentTracking,
        o.CommunicationTracking,
        o.ScreenshotCapture,
        o.AutoScreenshotCapture,
        o.MeetingDetection,
        o.DeviceTracking,
        o.WorkLocationVerification,
        o.IdentityVerification,
        o.Biometric,
        o.IdleThresholdMinutes,
        o.AllowedRadiusMeters,
        o.OverrideReason
    }, JsonOptions);

    public static AuditLog Entry(
        Guid tenantId, Guid? userId, string action, string resourceType, Guid resourceId,
        string? oldValuesJson, string? newValuesJson, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = userId,
        Action = action,
        ResourceType = resourceType,
        ResourceId = resourceId,
        OldValuesJson = oldValuesJson,
        NewValuesJson = newValuesJson,
        CreatedAt = at
    };
}
