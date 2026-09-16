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

    public async Task<bool> IsRequiredForCurrentUserAsync(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return false;

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        if (!await _entitlements.IsModuleEnabledAsync(tenantId, MonitoringModuleKey, ct))
            return false;

        foreach (var capability in Enum.GetValues<MonitoringCapability>())
        {
            var enabled = _currentUser.LegalEntityId is { } legalEntityId
                ? await _toggles.IsEnabledAsync(tenantId, userId, legalEntityId, capability, ct)
                : await _toggles.IsEnabledAsync(tenantId, userId, capability, ct);

            if (enabled)
                return true;
        }

        return false;
    }
}
