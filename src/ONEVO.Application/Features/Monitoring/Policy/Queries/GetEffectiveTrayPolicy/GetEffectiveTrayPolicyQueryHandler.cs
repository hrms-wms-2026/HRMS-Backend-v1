using System.Security.Cryptography;
using System.Text;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Policy.DTOs;

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

    public GetEffectiveTrayPolicyQueryHandler(
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IMonitoringToggleResolver toggles,
        IDateTimeProvider clock)
    {
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
        _toggles = toggles;
        _clock = clock;
    }

    public async Task<Result<TrayAgentPolicyDto>> Handle(
        GetEffectiveTrayPolicyQuery request,
        CancellationToken cancellationToken)
    {
        if (!_device.IsAuthenticated
            || _device.TenantId == Guid.Empty
            || _device.UserId == Guid.Empty
            || _device.DeviceRegistrationId == Guid.Empty)
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

        var activityEnabled = await _toggles.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.ActivityMonitoring, cancellationToken);
        var appUsageEnabled = await _toggles.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.ApplicationTracking, cancellationToken);
        var screenshotEnabled = await _toggles.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.ScreenshotCapture, cancellationToken);
        var autoScreenshotEnabled = await _toggles.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.AutoScreenshotCapture, cancellationToken);
        var cameraEnabled = await _toggles.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.IdentityVerification, cancellationToken);
        var idleThresholdMinutes = await _toggles.GetIdleThresholdMinutesAsync(
            tenantId, employeeId, cancellationToken);

        var inactivityEnabled = activityEnabled && screenshotEnabled && autoScreenshotEnabled;
        var now = _clock.UtcNow;

        return Result<TrayAgentPolicyDto>.Success(new TrayAgentPolicyDto(
            ComputeVersion(activityEnabled, appUsageEnabled, screenshotEnabled, autoScreenshotEnabled, cameraEnabled, idleThresholdMinutes),
            activityEnabled,
            appUsageEnabled,
            screenshotEnabled,
            inactivityEnabled,
            cameraEnabled,
            idleThresholdMinutes,
            now.Add(PolicyValidity)));
    }

    internal static string ComputeVersion(
        bool activityEnabled,
        bool appUsageEnabled,
        bool screenshotEnabled,
        bool autoScreenshotEnabled,
        bool cameraEnabled,
        int idleThresholdMinutes)
    {
        var fingerprint =
            $"{activityEnabled}:{appUsageEnabled}:{screenshotEnabled}:{autoScreenshotEnabled}:{cameraEnabled}:{idleThresholdMinutes}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))[..16];
    }
}
