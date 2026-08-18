using System.Text;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Notifications;

public class NotificationDispatcher : INotificationDispatcher
{
    private readonly INotificationRepository _notifications;

    public NotificationDispatcher(INotificationRepository notifications) => _notifications = notifications;

    public Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken ct = default)
        => throw new NotImplementedException("Use SendTemplatedAsync for Work Management notification templates.");

    public Task SendToTenantAsync(Guid tenantId, string eventName, object payload, CancellationToken ct = default)
        => throw new NotImplementedException("Use SendTemplatedAsync for Work Management notification templates.");

    public Task SendToGroupAsync(string groupName, string eventName, object payload, CancellationToken ct = default)
        => throw new NotImplementedException("Use SendTemplatedAsync for Work Management notification templates.");

    public async Task SendTemplatedAsync(
        Guid tenantId, Guid recipientUserId, string templateCode,
        IReadOnlyDictionary<string, string> placeholders,
        string? relatedEntityType = null, Guid? relatedEntityId = null,
        CancellationToken ct = default)
    {
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
