using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Settings.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Settings.ServiceInterfaces;

public interface IMonitoringPolicyConfigurationService
{
    /// <param name="legalEntityId">The caller's current active company (never accepted from the
    /// request). Department/position overrides outside this legal entity are omitted from the
    /// response; role overrides and the tenant-wide company default are unaffected (roles and
    /// the company default are not legal-entity-scoped concepts in Phase 1). Null hides all
    /// department/position overrides (fail closed - no active company context to attribute them to).</param>
    Task<MonitoringPolicyConfigurationResponse> GetAsync(
        Guid tenantId, Guid? legalEntityId, CancellationToken ct = default);

    /// <param name="legalEntityId">The caller's current active company (never accepted from the
    /// request). For department/position scope, the target must belong to this legal entity or
    /// the upsert fails as NotFound; null always fails closed for those two scopes. Role scope is
    /// tenant-wide and unaffected.</param>
    Task<Result<MonitoringPolicyOverrideResponse>> UpsertOverrideAsync(
        Guid tenantId,
        Guid actorId,
        string scopeType,
        Guid scopeId,
        Guid? legalEntityId,
        MonitoringPolicyOverrideRequest request,
        CancellationToken ct = default);

    /// <param name="legalEntityId">Same legal-entity guard as <see cref="UpsertOverrideAsync"/>,
    /// applied so a caller cannot delete a department/position override that belongs to a
    /// different company than their current active one.</param>
    Task<Result> DeleteOverrideAsync(
        Guid tenantId,
        string scopeType,
        Guid scopeId,
        Guid? legalEntityId,
        CancellationToken ct = default);
}

public sealed record MonitoringPolicyOverrideRequest(
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
    string? OverrideReason);
