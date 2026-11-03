using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;

public interface INotificationDeliveryRepository
{
    /// <summary>True if this (tenant, event, recipient, channel) delivery already exists, saved or pending in this unit of work.</summary>
    Task<bool> ExistsAsync(Guid tenantId, Guid eventId, Guid recipientUserId, NotificationChannels channel, CancellationToken ct = default);
    Task AddAsync(NotificationDelivery delivery, CancellationToken ct = default);
    Task<NotificationDelivery?> GetTrackedAsync(Guid deliveryId, CancellationToken ct = default);
    /// <summary>Pending, unexpired tray deliveries for one recipient, oldest first.</summary>
    Task<IReadOnlyList<NotificationDelivery>> ListPendingTrayAsync(Guid tenantId, Guid recipientUserId, DateTimeOffset now, int max, CancellationToken ct = default);
    /// <summary>Marks every pending tray delivery past its expiry as Expired (all tenants; caller runs in admin mode).</summary>
    Task<int> ExpirePendingTrayAsync(DateTimeOffset now, CancellationToken ct = default);
}
