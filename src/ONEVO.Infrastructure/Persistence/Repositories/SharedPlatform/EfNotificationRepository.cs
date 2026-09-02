using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform;

public class EfNotificationRepository : INotificationRepository
{
    private readonly ApplicationDbContext _db;

    public EfNotificationRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(Notification notification, CancellationToken ct = default)
        => await _db.Notifications.AddAsync(notification, ct);

    public async Task<IReadOnlyList<Notification>> GetByRecipientAsync(Guid tenantId, Guid recipientUserId, bool unreadOnly, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Notifications.AsNoTracking()
            .Where(n => n.TenantId == tenantId && n.RecipientUserId == recipientUserId);
        if (unreadOnly) query = query.Where(n => !n.IsRead);
        return await query.OrderByDescending(n => n.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
    }

    public async Task<int> GetUnreadCountAsync(Guid tenantId, Guid recipientUserId, CancellationToken ct = default)
        => await _db.Notifications.CountAsync(n => n.TenantId == tenantId && n.RecipientUserId == recipientUserId && !n.IsRead, ct);

    public async Task<Notification?> GetTrackedByIdForRecipientAsync(Guid tenantId, Guid id, Guid recipientUserId, CancellationToken ct = default)
        => await _db.Notifications.FirstOrDefaultAsync(n => n.TenantId == tenantId && n.Id == id && n.RecipientUserId == recipientUserId, ct);

    public async Task MarkAllReadAsync(Guid tenantId, Guid recipientUserId, CancellationToken ct = default)
        => await _db.Notifications
            .Where(n => n.TenantId == tenantId && n.RecipientUserId == recipientUserId && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow), ct);

    public async Task<NotificationTemplate?> GetTemplateByCodeAsync(string code, CancellationToken ct = default)
        => await _db.NotificationTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Code == code, ct);

    public async Task AddTemplateRangeAsync(IReadOnlyList<NotificationTemplate> templates, CancellationToken ct = default)
        => await _db.NotificationTemplates.AddRangeAsync(templates, ct);

    public async Task<bool> AnyTemplatesExistAsync(CancellationToken ct = default)
        => await _db.NotificationTemplates.AnyAsync(ct);

    public async Task<bool> ExistsAsync(
        Guid tenantId, Guid recipientUserId, string templateCode,
        string relatedEntityType, Guid relatedEntityId, CancellationToken ct = default)
        => await _db.Notifications.AsNoTracking().AnyAsync(n =>
            n.TenantId == tenantId
            && n.RecipientUserId == recipientUserId
            && n.TemplateCode == templateCode
            && n.RelatedEntityType == relatedEntityType
            && n.RelatedEntityId == relatedEntityId, ct);
}
