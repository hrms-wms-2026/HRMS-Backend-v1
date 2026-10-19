using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;

namespace ONEVO.Application.Features.Inbox.OutboxHandlers;

public sealed record InboxChangedPayload(Guid TenantId, IReadOnlyList<Guid> EmployeeIds, string Source, string Kind, Guid Id);

/// <summary>Pushes inboxChanged once the change that caused it has committed (the outbox only runs
/// committed messages), so an open Inbox never refetches before the data is there.</summary>
public sealed class InboxChangedOutboxHandler : IOutboxMessageHandler
{
    private readonly IInboxRealtimePublisher _realtime;

    public InboxChangedOutboxHandler(IInboxRealtimePublisher realtime) => _realtime = realtime;

    public string Type => OutboxMessageTypes.InboxChanged;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<InboxChangedPayload>(payloadJson)
            ?? throw new InvalidOperationException("inbox_changed payload is empty.");

        await _realtime.InboxChangedAsync(payload.TenantId, payload.EmployeeIds, new ItemRef(payload.Source, payload.Kind, payload.Id), ct);
    }
}
