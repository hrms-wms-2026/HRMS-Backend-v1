using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;

public sealed record WorkNotificationPayload(
    Guid TenantId,
    Guid RecipientUserId,
    string TemplateCode,
    Dictionary<string, string> Placeholders,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    Guid? EventId = null);

/// <summary>
/// Dispatches a templated notification from the outbox. Orchestrator-owned events are de-duplicated by the
/// payload EventId (set at enqueue); payloads queued before EventId existed get one derived from their content,
/// so a retry of the same message maps to the same deliveries.
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

        await _notifications.SendTemplatedEventAsync(
            payload.EventId ?? StableId(payloadJson),
            payload.TenantId, payload.RecipientUserId, payload.TemplateCode, payload.Placeholders,
            payload.RelatedEntityType, payload.RelatedEntityId, ct);
    }

    private static Guid StableId(string payloadJson)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)).AsSpan(0, 16));
}
