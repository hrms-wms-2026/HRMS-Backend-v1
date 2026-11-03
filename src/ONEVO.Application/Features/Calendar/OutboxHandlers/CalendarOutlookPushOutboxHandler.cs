using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Application.Features.Calendar.OutboxHandlers;

/// <summary>Push one event to the organizer's Outlook (<see cref="EventId"/>), or delete an already-pushed one
/// (<see cref="DeleteExternalEventId"/>). Outlook then emails the attendees in place of ONEVO.</summary>
public sealed record CalendarOutlookPushPayload(Guid TenantId, Guid ConnectionId, Guid? EventId, string? DeleteExternalEventId = null);

/// <summary>Runs after commit, so the pushed event already has its participants and guests. A failure is rethrown
/// so the outbox retries - the attendees' invite depends on it. Pushing the same event again just PATCHes it.</summary>
public sealed class CalendarOutlookPushOutboxHandler(ICalendarSyncService syncService) : IOutboxMessageHandler
{
    public string Type => OutboxMessageTypes.CalendarOutlookPush;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<CalendarOutlookPushPayload>(payloadJson)
            ?? throw new InvalidOperationException("calendar_outlook_push payload is empty.");

        if (!string.IsNullOrEmpty(payload.DeleteExternalEventId))
            await syncService.DeleteOutlookEventAsync(payload.TenantId, payload.ConnectionId, payload.DeleteExternalEventId, ct);
        else if (payload.EventId is Guid eventId)
            await syncService.PushOutlookEventAsync(payload.TenantId, payload.ConnectionId, eventId, ct);
    }
}
