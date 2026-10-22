namespace ONEVO.Application.Features.Monitoring.PermissionConsent.Commands.RecordPermissionConsent;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.PermissionConsent.DTOs;
using ONEVO.Application.Features.Monitoring.PermissionConsent.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.PermissionConsent.Entities;

public sealed class RecordPermissionConsentCommandHandler(
    ITrayCurrentDevice device,
    ITenantRepository tenants,
    ITenantContextSwitcher tenantSwitcher,
    IMonitoringPermissionConsentRepository consents,
    ITrayEmployeeIdentityResolver employeeIdentity)
    : IRequestHandler<RecordPermissionConsentCommand, Result<PermissionConsentStatusDto>>
{
    public async Task<Result<PermissionConsentStatusDto>> Handle(
        RecordPermissionConsentCommand request, CancellationToken ct)
    {
        if (!device.IsAuthenticated || device.TenantId == Guid.Empty || device.UserId == Guid.Empty)
            return Result<PermissionConsentStatusDto>.Failure("A valid tray device token is required.", 401);

        var tenant = await tenants.GetByIdAsync(device.TenantId, ct);
        if (tenant is null)
            return Result<PermissionConsentStatusDto>.Failure("Tenant not found.", 401);

        // The insert must run in this tenant's RLS context or WITH CHECK rejects it.
        await tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

        var employeeId = await employeeIdentity.ResolveEmployeeIdAsync(
            device.TenantId, device.UserId, device.LegalEntityId, ct);

        var now = DateTimeOffset.UtcNow;
        var consent = new MonitoringPermissionConsent
        {
            Id = Guid.NewGuid(),
            TenantId = device.TenantId,
            EmployeeId = employeeId,
            DeviceRegistrationId = device.DeviceRegistrationId,
            AcceptedAt = now,
            ScreenMonitoring = request.ScreenMonitoring,
            AppTracking = request.AppTracking,
            LocationAccess = request.LocationAccess,
            CameraAccess = request.CameraAccess,
            Notifications = request.Notifications,
            KeyboardMouse = request.KeyboardMouse,
            CreatedAt = now
        };

        await consents.AddAsync(consent, ct);
        await consents.SaveChangesAsync(ct);

        return Result<PermissionConsentStatusDto>.Success(new PermissionConsentStatusDto(
            true, consent.AcceptedAt,
            consent.ScreenMonitoring, consent.AppTracking, consent.LocationAccess,
            consent.CameraAccess, consent.Notifications, consent.KeyboardMouse));
    }
}
