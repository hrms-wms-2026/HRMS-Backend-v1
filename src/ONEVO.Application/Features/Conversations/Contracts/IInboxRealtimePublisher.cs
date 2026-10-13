using ONEVO.Application.Features.Conversations.DTOs;

namespace ONEVO.Application.Features.Conversations.Contracts;

/// <summary>Pushes inbox/conversation events to connected clients. Callers publish only after the
/// database commit; implementations must not throw (a realtime failure never fails a request).</summary>
public interface IInboxRealtimePublisher
{
    Task MessageCreatedAsync(Guid conversationId, ConversationMessageResponse message, CancellationToken ct = default);
    Task ParticipantAddedAsync(Guid conversationId, ConversationParticipantResponse participant, CancellationToken ct = default);
    Task InboxChangedAsync(IReadOnlyCollection<Guid> employeeIds, ItemRef item, CancellationToken ct = default);
}
