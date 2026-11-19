namespace ONEVO.Application.Features.Monitoring.PermissionConsent.Queries.GetPermissionConsentStatus;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.PermissionConsent.DTOs;
using ONEVO.Application.Features.Monitoring.PermissionConsent.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

public sealed class GetPermissionConsentStatusQueryHandler(
    ITrayCurrentDevice device,
    ITenantRepository tenants,
    ITenantContextSwitcher tenantSwitcher,
    IMonitoringPermissionConsentRepository consents,
    ITrayEmployeeIdentityResolver employeeIdentity)
    : IRequestHandler<GetPermissionConsentStatusQuery, Result<PermissionConsentStatusDto>>
{
    public async Task<Result<PermissionConsentStatusDto>> Handle(
        GetPermissionConsentStatusQuery request, CancellationToken ct)
    {
        if (!device.IsAuthenticated || device.TenantId == Guid.Empty || device.UserId == Guid.Empty)
            return Result<PermissionConsentStatusDto>.Failure("A valid tray device token is required.", 401);

        var tenant = await tenants.GetByIdAsync(device.TenantId, ct);
        if (tenant is null)
            return Result<PermissionConsentStatusDto>.Failure("Tenant not found.", 401);

        await tenantSwitcher.SwitchToTenantAsync(
            new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);

        var employeeId = await employeeIdentity.ResolveEmployeeIdAsync(
            device.TenantId, device.UserId, device.LegalEntityId, ct);
        var latest = await consents.GetLatestAsync(device.TenantId, employeeId, ct);

        if (latest is null)
            return Result<PermissionConsentStatusDto>.Success(PermissionConsentStatusDto.None);

        return Result<PermissionConsentStatusDto>.Success(new PermissionConsentStatusDto(
            true, latest.AcceptedAt,
            latest.ScreenMonitoring, latest.AppTracking, latest.LocationAccess,
            latest.CameraAccess, latest.Notifications, latest.KeyboardMouse));
    }
}
