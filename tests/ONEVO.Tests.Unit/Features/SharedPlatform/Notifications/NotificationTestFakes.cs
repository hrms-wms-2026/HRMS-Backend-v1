using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Delivery;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.Notifications;

/// <summary>List-backed stand-ins for the notification repositories, shared by the notification tests.</summary>
internal static class NotificationTestFakes
{
    internal sealed class FakeDeliveryRepository : INotificationDeliveryRepository
    {
        public List<NotificationDelivery> Deliveries { get; } = [];

        public Task<bool> ExistsAsync(Guid tenantId, Guid eventId, Guid recipientUserId, NotificationChannels channel, CancellationToken ct = default)
            => Task.FromResult(Deliveries.Any(d => d.TenantId == tenantId && d.EventId == eventId && d.RecipientUserId == recipientUserId && d.Channel == channel));
        public Task AddAsync(NotificationDelivery delivery, CancellationToken ct = default) { Deliveries.Add(delivery); return Task.CompletedTask; }
        public Task<NotificationDelivery?> GetTrackedAsync(Guid deliveryId, CancellationToken ct = default)
            => Task.FromResult(Deliveries.FirstOrDefault(d => d.Id == deliveryId));
    }

    internal sealed class FakeTemplateStore : INotificationTemplateStore
    {
        public List<NotificationTemplate> Templates { get; } = [];
        public List<NotificationTemplateVersion> Versions { get; } = [];

        public Task<IReadOnlyList<NotificationTemplate>> ListAsync(string? eventKey, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NotificationTemplate>>(Templates.Where(t => eventKey == null || t.EventKey == eventKey).ToList());
        public Task<NotificationTemplate?> GetTrackedAsync(Guid templateId, CancellationToken ct = default)
            => Task.FromResult(Templates.FirstOrDefault(t => t.Id == templateId));
        public Task<bool> CodeExistsAsync(string code, CancellationToken ct = default) => Task.FromResult(Templates.Any(t => t.Code == code));
        public Task<NotificationTemplate?> GetDefaultForEventAsync(string eventKey, CancellationToken ct = default)
            => Task.FromResult(Templates.FirstOrDefault(t => t.Code == eventKey));
        public Task<IReadOnlyList<NotificationTemplateVersion>> ListVersionsAsync(Guid templateId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NotificationTemplateVersion>>(Versions.Where(v => v.TemplateId == templateId).ToList());
        public Task<NotificationTemplateVersion?> GetVersionAsync(Guid versionId, CancellationToken ct = default)
            => Task.FromResult(Versions.FirstOrDefault(v => v.Id == versionId));
        public Task<NotificationTemplateVersion?> GetTrackedDraftAsync(Guid templateId, CancellationToken ct = default)
            => Task.FromResult(Versions.FirstOrDefault(v => v.TemplateId == templateId && v.Status == NotificationTemplateVersionStatus.Draft));
        public Task<int> GetLatestVersionNumberAsync(Guid templateId, CancellationToken ct = default)
            => Task.FromResult(Versions.Where(v => v.TemplateId == templateId).Select(v => v.Version).DefaultIfEmpty(0).Max());
        public Task AddTemplateAsync(NotificationTemplate template, CancellationToken ct = default) { Templates.Add(template); return Task.CompletedTask; }
        public Task AddVersionAsync(NotificationTemplateVersion version, CancellationToken ct = default) { Versions.Add(version); return Task.CompletedTask; }
    }

    internal sealed class FakeRuleRepository : ITenantNotificationRuleRepository
    {
        public List<TenantNotificationRule> Rules { get; } = [];

        public Task<IReadOnlyList<TenantNotificationRule>> ListAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantNotificationRule>>(Rules.Where(r => r.TenantId == tenantId).ToList());
        public Task<TenantNotificationRule?> GetTrackedAsync(Guid tenantId, string eventKey, CancellationToken ct = default)
            => Task.FromResult(Rules.FirstOrDefault(r => r.TenantId == tenantId && r.EventKey == eventKey));
        public Task<TenantNotificationRule?> GetAsync(Guid tenantId, string eventKey, CancellationToken ct = default)
            => GetTrackedAsync(tenantId, eventKey, ct);
        public Task AddAsync(TenantNotificationRule rule, CancellationToken ct = default) { Rules.Add(rule); return Task.CompletedTask; }
        public void Remove(TenantNotificationRule rule) => Rules.Remove(rule);
    }

    internal sealed class FakeCache : ICacheService
    {
        private readonly Dictionary<string, object?> _items = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
            => Task.FromResult(_items.TryGetValue(key, out var v) ? (T?)v : default);
        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) { _items[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken ct = default) { _items.Remove(key); return Task.CompletedTask; }
        public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default) => Task.CompletedTask;
    }
}
