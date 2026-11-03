using System.Text;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Application.Features.SharedPlatform.Notifications.Delivery;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Notifications;

/// <summary>
/// Compatibility adapter and single cut-over point for producers that call SendTemplatedAsync directly: events
/// owned by the orchestrator (NotificationEventCatalog) are routed there per tenant settings; every other event
/// keeps the legacy in-app insert. An event never goes both ways.
/// </summary>
public class NotificationDispatcher : INotificationDispatcher
{
    private readonly INotificationRepository _notifications;
    private readonly INotificationOrchestrator? _orchestrator;

    public NotificationDispatcher(INotificationRepository notifications, INotificationOrchestrator? orchestrator = null)
    {
        _notifications = notifications;
        _orchestrator = orchestrator;
    }

    public Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken ct = default)
        => throw new NotImplementedException("Use SendTemplatedAsync for Work Management notification templates.");

    public Task SendToTenantAsync(Guid tenantId, string eventName, object payload, CancellationToken ct = default)
        => throw new NotImplementedException("Use SendTemplatedAsync for Work Management notification templates.");

    public Task SendToGroupAsync(string groupName, string eventName, object payload, CancellationToken ct = default)
        => throw new NotImplementedException("Use SendTemplatedAsync for Work Management notification templates.");

    public Task SendTemplatedAsync(
        Guid tenantId, Guid recipientUserId, string templateCode,
        IReadOnlyDictionary<string, string> placeholders,
        string? relatedEntityType = null, Guid? relatedEntityId = null,
        CancellationToken ct = default)
        => SendAsync(null, tenantId, recipientUserId, templateCode, placeholders, relatedEntityType, relatedEntityId, ct);

    public Task SendTemplatedEventAsync(
        Guid eventId, Guid tenantId, Guid recipientUserId, string templateCode,
        IReadOnlyDictionary<string, string> placeholders,
        string? relatedEntityType = null, Guid? relatedEntityId = null,
        CancellationToken ct = default)
        => SendAsync(eventId, tenantId, recipientUserId, templateCode, placeholders, relatedEntityType, relatedEntityId, ct);

    private async Task SendAsync(
        Guid? eventId, Guid tenantId, Guid recipientUserId, string templateCode,
        IReadOnlyDictionary<string, string> placeholders,
        string? relatedEntityType, Guid? relatedEntityId, CancellationToken ct)
    {
        if (_orchestrator is not null && NotificationEventCatalog.TryGet(templateCode, out var definition) && definition.OrchestratorOwned)
        {
            // Direct callers run inside their business transaction, so a new id here is stable for the occurrence;
            // outbox-driven callers pass the id persisted in their payload.
            await _orchestrator.EnqueueAsync(new NotificationDispatchRequest(
                eventId ?? Guid.NewGuid(), templateCode, tenantId, recipientUserId, placeholders,
                relatedEntityType, relatedEntityId), ct);
            return;
        }

        var template = await _notifications.GetTemplateByCodeAsync(templateCode, ct);
        if (template is null || !template.InAppEnabled)
            return;

        var notification = new Notification
        {
            Id = Guid.NewGuid(), TenantId = tenantId, RecipientUserId = recipientUserId,
            TemplateCode = templateCode, Title = Render(template.InAppTitleTemplate, placeholders),
            Body = Render(template.InAppBodyTemplate, placeholders),
            RelatedEntityType = relatedEntityType, RelatedEntityId = relatedEntityId,
            IsRead = false, CreatedAt = DateTimeOffset.UtcNow
        };

        await _notifications.AddAsync(notification, ct);
        // Mail half intentionally omitted: template.MailEnabled is false by default in this
        // slice (spec §6.4) - wiring IOutboxWriter here is explicitly deferred, not forgotten.
    }

    private static string Render(string template, IReadOnlyDictionary<string, string> placeholders)
    {
        var sb = new StringBuilder(template);
        foreach (var (key, value) in placeholders)
            sb.Replace($"{{{{{key}}}}}", value);
        return sb.ToString();
    }
}
