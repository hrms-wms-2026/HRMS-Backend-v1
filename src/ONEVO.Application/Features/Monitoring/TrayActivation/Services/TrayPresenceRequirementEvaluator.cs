using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Services;

public sealed class TrayPresenceRequirementEvaluator : ITrayPresenceRequirementEvaluator
{
    /// <summary>Module catalog key of the monitoring module (see PermissionSeeder's monitoring:* permissions).</summary>
    public const string MonitoringModuleKey = "activity_monitoring";

    private readonly ICurrentUser _currentUser;
    private readonly IModuleEntitlementService _entitlements;
    private readonly IMonitoringToggleResolver _toggles;

    public TrayPresenceRequirementEvaluator(
        ICurrentUser currentUser,
        IModuleEntitlementService entitlements,
        IMonitoringToggleResolver toggles)
    {
        _currentUser = currentUser;
        _entitlements = entitlements;
        _toggles = toggles;
    }

    /// <summary>
    /// Capabilities that gate the tray requirement. Deliberately not
    /// <c>Enum.GetValues&lt;MonitoringCapability&gt;()</c>: only these four have a checkbox on the
    /// Monitoring Configuration page (MonitoringComponent's company-default grid). The rest -
    /// AutoScreenshotCapture, DeviceTracking, DocumentTracking, CommunicationTracking,
    /// MeetingDetection, IdentityVerification, Biometric - have no admin-facing control, so an
    /// admin can never turn them off; looping over all of them forced every employee in a tenant
    /// to install the tray app the moment any one of those hidden fields happened to be true
    /// (e.g. a dev seed default) with no way to fix it. AutoScreenshotCapture is covered by
    /// ScreenshotCapture (one screenshot toggle, not two). DeviceTracking is covered by
    /// ApplicationTracking (device-state snapshots are part of app-usage tracking, not a
    /// separate capability). DocumentTracking/CommunicationTracking/MeetingDetection belong to
    /// the not-yet-built detected-application-category feature and must not gate anything until
    /// that exists. IdentityVerification does not belong here at all - see below.
    /// </summary>
    private static readonly MonitoringCapability[] GatingCapabilities =
    [
        MonitoringCapability.ActivityMonitoring,
        MonitoringCapability.ApplicationTracking,
        MonitoringCapability.ScreenshotCapture,
        MonitoringCapability.WorkLocationVerification
    ];

    public async Task<bool> IsRequiredForCurrentUserAsync(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return false;

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        if (!await _entitlements.IsModuleEnabledAsync(tenantId, MonitoringModuleKey, ct))
            return false;

        foreach (var capability in GatingCapabilities)
        {
            var enabled = _currentUser.LegalEntityId is { } legalEntityId
                ? await _toggles.IsEnabledAsync(tenantId, userId, legalEntityId, capability, ct)
                : await _toggles.IsEnabledAsync(tenantId, userId, capability, ct);

            if (enabled)
                return true;
        }

        // Identity verification is owned by the employee's Work Mode ("photo required"), not by
        // a MonitoringFeatureToggles flag - see IMonitoringToggleResolver.IsPhotoRequiredAsync.
        return _currentUser.LegalEntityId is { } activeLegalEntityId
            ? await _toggles.IsPhotoRequiredAsync(tenantId, userId, activeLegalEntityId, ct)
            : await _toggles.IsPhotoRequiredAsync(tenantId, userId, ct);
    }
}
