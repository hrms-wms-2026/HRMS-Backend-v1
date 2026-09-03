using System.Security.Cryptography;
using System.Text;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Policy.DTOs;
using ONEVO.Application.Features.TimeAttendance.Services;

namespace ONEVO.Application.Features.Monitoring.Policy.Queries.GetEffectiveTrayPolicy;

public sealed class GetEffectiveTrayPolicyQueryHandler
    : IRequestHandler<GetEffectiveTrayPolicyQuery, Result<TrayAgentPolicyDto>>
{
    internal static readonly TimeSpan PolicyValidity = TimeSpan.FromHours(1);

    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IMonitoringToggleResolver _toggles;
    private readonly IDateTimeProvider _clock;
    private readonly IAttendanceTodayStateService _todayState;

    public GetEffectiveTrayPolicyQueryHandler(
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IMonitoringToggleResolver toggles,
        IDateTimeProvider clock,
        IAttendanceTodayStateService todayState)
    {
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _toggles = toggles;
        _clock = clock;
        _todayState = todayState;
    }

    public async Task<Result<TrayAgentPolicyDto>> Handle(
        GetEffectiveTrayPolicyQuery request,
        CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty
            || _device.LegalEntityId is null)
        {
            return Result<TrayAgentPolicyDto>.Failure("A valid tray device token is required.", 401);
        }

        var tenant = await _tenants.GetByIdAsync(_device.TenantId, cancellationToken);
        if (tenant is null)
            return Result<TrayAgentPolicyDto>.Failure("Tenant not found.", 401);

        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var tenantId = _device.TenantId;
        var employeeId = _device.UserId;
        var legalEntityId = _device.LegalEntityId.Value;

        var locationEnabled = await _toggles.IsEnabledAsync(
            tenantId, employeeId, legalEntityId, MonitoringCapability.WorkLocationVerification, cancellationToken);
        var activityEnabled = await _toggles.IsEnabledAsync(
            tenantId, employeeId, legalEntityId, MonitoringCapability.ActivityMonitoring, cancellationToken);

        var appUsageEnabled = await _toggles.IsEnabledAsync(
            tenantId, employeeId, legalEntityId, MonitoringCapability.ApplicationTracking, cancellationToken);
        var screenshotEnabled = await _toggles.IsEnabledAsync(
            tenantId, employeeId, legalEntityId, MonitoringCapability.ScreenshotCapture, cancellationToken);
        var autoScreenshotEnabled = await _toggles.IsEnabledAsync(
            tenantId, employeeId, legalEntityId, MonitoringCapability.AutoScreenshotCapture, cancellationToken);
        var cameraEnabled = await _toggles.IsEnabledAsync(
            tenantId, employeeId, legalEntityId, MonitoringCapability.IdentityVerification, cancellationToken);
        var idleThresholdMinutes = await _toggles.GetIdleThresholdMinutesAsync(
            tenantId, employeeId, legalEntityId, cancellationToken);

        var inactivityEnabled = activityEnabled && screenshotEnabled && autoScreenshotEnabled;
        var now = _clock.UtcNow;

        var todayContextResult = await _todayState.ResolveContextAsync(tenantId, employeeId, cancellationToken);
        var trayClockInEnabled = todayContextResult.IsSuccess
            && todayContextResult.Value!.AllowedClockInMethods.DesktopTray;

        return Result<TrayAgentPolicyDto>.Success(new TrayAgentPolicyDto(
            ComputeVersion(locationEnabled, activityEnabled, appUsageEnabled, screenshotEnabled, autoScreenshotEnabled, cameraEnabled, idleThresholdMinutes, trayClockInEnabled),
            activityEnabled,
            appUsageEnabled,
            screenshotEnabled,
            inactivityEnabled,
            cameraEnabled,
            idleThresholdMinutes,
            now.Add(PolicyValidity),
            EffectiveScope: "employee",
            LocationTrackingEnabled: locationEnabled,
            TrayClockInEnabled: trayClockInEnabled));
    }

    internal static string ComputeVersion(
        bool locationEnabled,
        bool activityEnabled,
        bool appUsageEnabled,
        bool screenshotEnabled,
        bool autoScreenshotEnabled,
        bool cameraEnabled,
        int idleThresholdMinutes,
        bool trayClockInEnabled)
    {
        var fingerprint =
            $"{locationEnabled}:{activityEnabled}:{appUsageEnabled}:{screenshotEnabled}:{autoScreenshotEnabled}:{cameraEnabled}:{idleThresholdMinutes}:{trayClockInEnabled}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))[..16];
    }
}
