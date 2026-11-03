using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;

/// <summary>Global notification templates and their versions. "Tracked" reads return entities the
/// unit of work will save.</summary>
public interface INotificationTemplateStore
{
    Task<IReadOnlyList<NotificationTemplate>> ListAsync(string? eventKey, CancellationToken ct = default);
    Task<NotificationTemplate?> GetTrackedAsync(Guid templateId, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, CancellationToken ct = default);
    /// <summary>The product-default template for an event (Code == EventKey).</summary>
    Task<NotificationTemplate?> GetDefaultForEventAsync(string eventKey, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationTemplateVersion>> ListVersionsAsync(Guid templateId, CancellationToken ct = default);
    Task<NotificationTemplateVersion?> GetVersionAsync(Guid versionId, CancellationToken ct = default);
    Task<NotificationTemplateVersion?> GetTrackedDraftAsync(Guid templateId, CancellationToken ct = default);
    Task<int> GetLatestVersionNumberAsync(Guid templateId, CancellationToken ct = default);
    Task AddTemplateAsync(NotificationTemplate template, CancellationToken ct = default);
    Task AddVersionAsync(NotificationTemplateVersion version, CancellationToken ct = default);
}
