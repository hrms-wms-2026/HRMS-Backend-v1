using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Tray;

public sealed record TrayDeliveryDto(
    Guid DeliveryId,
    string EventKey,
    string Title,
    string Message,
    string ActionKey,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    DateTimeOffset? ExpiresAt);

internal static class TrayDeliveryDevice
{
    /// <summary>
    /// Tray requests reach the root domain, which runs in system mode, and notification_deliveries is under
    /// FORCE RLS - so every tray handler first switches to the device's own tenant (same as the legacy
    /// monitoring tray handlers). Returns false when the device or its tenant is not valid.
    /// </summary>
    public static async Task<bool> EnterDeviceTenantAsync(
        ITrayCurrentDevice device, ITenantRepository tenants, ITenantContextSwitcher switcher, CancellationToken ct)
    {
        if (!device.IsAuthenticated || device.TenantId == Guid.Empty || device.UserId == Guid.Empty)
            return false;

        var tenant = await tenants.GetByIdAsync(device.TenantId, ct);
        if (tenant is null)
            return false;

        await switcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null), ct);
        return true;
    }
}
