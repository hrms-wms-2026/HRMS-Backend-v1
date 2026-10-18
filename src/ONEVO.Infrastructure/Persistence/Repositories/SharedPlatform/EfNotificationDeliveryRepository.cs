using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform;

public sealed class EfNotificationDeliveryRepository : INotificationDeliveryRepository
{
    private readonly ApplicationDbContext _db;

    public EfNotificationDeliveryRepository(ApplicationDbContext db) => _db = db;

    public async Task<bool> ExistsAsync(Guid tenantId, Guid eventId, Guid recipientUserId, NotificationChannels channel, CancellationToken ct = default)
    {
        // Pending in this unit of work (same event enqueued twice in one transaction) counts too; the unique
        // index ix_notification_deliveries_identity is the final guard against concurrent writers.
        if (_db.NotificationDeliveries.Local.Any(d => d.TenantId == tenantId && d.EventId == eventId
                && d.RecipientUserId == recipientUserId && d.Channel == channel))
            return true;

        return await _db.NotificationDeliveries.AnyAsync(d => d.TenantId == tenantId && d.EventId == eventId
            && d.RecipientUserId == recipientUserId && d.Channel == channel, ct);
    }

    public async Task AddAsync(NotificationDelivery delivery, CancellationToken ct = default)
        => await _db.NotificationDeliveries.AddAsync(delivery, ct);

    public Task<NotificationDelivery?> GetTrackedAsync(Guid deliveryId, CancellationToken ct = default)
        => _db.NotificationDeliveries.FirstOrDefaultAsync(d => d.Id == deliveryId, ct);
}
