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
    private readonly IMonitoringToggleResolver _toggleResolver;
    private readonly ITrayCurrentDevice _device;
    private readonly ITenantRepository _tenants;
    private readonly ITenantContextSwitcher _tenantSwitcher;
    private readonly IDateTimeProvider _clock;

    public GetEffectiveTrayPolicyQueryHandler(
        IMonitoringToggleResolver toggleResolver,
        ITrayCurrentDevice device,
        ITenantRepository tenants,
        ITenantContextSwitcher tenantSwitcher,
        IDateTimeProvider clock)
    {
        _toggleResolver = toggleResolver;
        _device = device;
        _tenants = tenants;
        _tenantSwitcher = tenantSwitcher;
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

        // Tray requests may hit the base host (system mode). Switch into the JWT
        // tenant so EF query filters + PostgreSQL RLS accept the read: the toggle
        // resolver queries tenant-owned tables (Employees, MonitoringFeatureToggles,
        // policy overrides, ...) that run under FORCE ROW LEVEL SECURITY.
        await _tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null),
            cancellationToken);

        var tenantId = _device.TenantId;
        // Phase 1: tray JWT binds to UserId; use it as the effective employeeId
        // until CoreHR employee master is always present for activated devices.
        var employeeId = _device.UserId;

        var activityEnabled = await _toggleResolver.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.ActivityMonitoring, cancellationToken);
        var appUsageEnabled = await _toggleResolver.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.ApplicationTracking, cancellationToken);
        var screenshotEnabled = await _toggleResolver.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.ScreenshotCapture, cancellationToken);
        var autoScreenshotEnabled = await _toggleResolver.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.AutoScreenshotCapture, cancellationToken);
        var cameraEnabled = await _toggleResolver.IsEnabledAsync(
            tenantId, employeeId, MonitoringCapability.IdentityVerification, cancellationToken);

        // Inactivity auto-capture is only meaningful when the underlying activity
        // signal and screenshot capabilities are both present.
        var inactivityEnabled = activityEnabled && screenshotEnabled && autoScreenshotEnabled;

        var fingerprint =
            $"{activityEnabled}:{appUsageEnabled}:{screenshotEnabled}:{autoScreenshotEnabled}:{cameraEnabled}";
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))[..16];

        var now = _clock.UtcNow;
        var dto = new TrayAgentPolicyDto(
            version,
            activityEnabled,
            appUsageEnabled,
            screenshotEnabled,
            inactivityEnabled,
            cameraEnabled,
            now.AddHours(1));

        return Result<TrayAgentPolicyDto>.Success(dto);
    }
}
