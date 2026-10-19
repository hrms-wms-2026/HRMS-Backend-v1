namespace ONEVO.Application.Common.ServiceInterfaces;

public interface INotificationDispatcher
{
    Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken ct = default);
    Task SendToTenantAsync(Guid tenantId, string eventName, object payload, CancellationToken ct = default);
    Task SendToGroupAsync(string groupName, string eventName, object payload, CancellationToken ct = default);

    /// <summary>Renders <paramref name="templateCode"/>'s in-app template against <paramref name="placeholders"/>
    /// and writes a Notification row for <paramref name="recipientUserId"/> if the template's InAppEnabled is true.
    /// No-op for the mail half in this slice (MailEnabled defaults false - see spec §6.4).
    /// Events owned by the notification orchestrator are routed through it instead (all channels, tenant settings).</summary>
    Task SendTemplatedAsync(
        Guid tenantId, Guid recipientUserId, string templateCode,
        IReadOnlyDictionary<string, string> placeholders,
        string? relatedEntityType = null, Guid? relatedEntityId = null,
        CancellationToken ct = default);

    /// <summary>Same as <see cref="SendTemplatedAsync"/> for a caller that knows a stable id for the occurrence (an
    /// outbox payload), so a retried message cannot create duplicate deliveries for orchestrator-owned events.</summary>
    Task SendTemplatedEventAsync(
        Guid eventId, Guid tenantId, Guid recipientUserId, string templateCode,
        IReadOnlyDictionary<string, string> placeholders,
        string? relatedEntityType = null, Guid? relatedEntityId = null,
        CancellationToken ct = default)
        => SendTemplatedAsync(tenantId, recipientUserId, templateCode, placeholders, relatedEntityType, relatedEntityId, ct);
}
