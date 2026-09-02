using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Common.RepositoryInterfaces;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken ct = default);
    Task<IReadOnlyList<Notification>> GetByRecipientAsync(Guid tenantId, Guid recipientUserId, bool unreadOnly, int page, int pageSize, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid tenantId, Guid recipientUserId, CancellationToken ct = default);
    Task<Notification?> GetTrackedByIdForRecipientAsync(Guid tenantId, Guid id, Guid recipientUserId, CancellationToken ct = default);
    Task MarkAllReadAsync(Guid tenantId, Guid recipientUserId, CancellationToken ct = default);
    Task<NotificationTemplate?> GetTemplateByCodeAsync(string code, CancellationToken ct = default);
    Task AddTemplateRangeAsync(IReadOnlyList<NotificationTemplate> templates, CancellationToken ct = default);
    Task<bool> AnyTemplatesExistAsync(CancellationToken ct = default);

    // Idempotency guard for jobs that may be retried or restarted: has this exact
    // (recipient, template, related entity) notification already been sent?
    Task<bool> ExistsAsync(
        Guid tenantId, Guid recipientUserId, string templateCode,
        string relatedEntityType, Guid relatedEntityId, CancellationToken ct = default);
}
