using ONEVO.Domain.Features.Monitoring.Notifications.Entities;

namespace ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken ct);

    /// <summary>Anti-spam check: true if a Notification of this type exists for this employee created after <paramref name="sinceUtc"/>.</summary>
    Task<bool> ExistsRecentAsync(
        Guid tenantId, Guid employeeId, NotificationType type, DateTimeOffset sinceUtc, CancellationToken ct);

    Task<IReadOnlyList<Notification>> GetPendingForTrayAsync(Guid tenantId, Guid employeeId, CancellationToken ct);

    Task<Notification?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct);

    Task<IReadOnlyList<Notification>> GetInboxAsync(
        Guid tenantId, Guid employeeId, int page, int pageSize, CancellationToken ct);

    Task<int> GetInboxTotalCountAsync(Guid tenantId, Guid employeeId, CancellationToken ct);

    void Update(Notification notification);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
