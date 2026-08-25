using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;

public sealed record WorkNotificationPayload(
    Guid TenantId,
    Guid RecipientUserId,
    string TemplateCode,
    Dictionary<string, string> Placeholders,
    string? RelatedEntityType,
    Guid? RelatedEntityId);

/// <summary>
/// Dispatches a templated in-app notification from the outbox. Safe to retry: SendTemplatedAsync
/// is a plain insert with no dedup, matching the pre-existing dispatcher property.
/// </summary>
public sealed class WorkNotificationOutboxHandler : IOutboxMessageHandler
{
    private readonly INotificationDispatcher _notifications;

    public WorkNotificationOutboxHandler(INotificationDispatcher notifications)
    {
        _notifications = notifications;
    }

    public string Type => OutboxMessageTypes.WorkNotification;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<WorkNotificationPayload>(payloadJson)
            ?? throw new InvalidOperationException("work_notification payload is empty.");

        await _notifications.SendTemplatedAsync(
            payload.TenantId, payload.RecipientUserId, payload.TemplateCode, payload.Placeholders,
            payload.RelatedEntityType, payload.RelatedEntityId, ct);
    }
}
