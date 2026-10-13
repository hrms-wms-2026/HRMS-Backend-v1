using ONEVO.Application.Features.Conversations.DTOs;

namespace ONEVO.Application.Features.Conversations.Contracts;

/// <summary>Pushes inbox/conversation events to the given employees' connected clients (never to a
/// conversation-wide channel, so only current participants receive a conversation's events). Callers
/// publish only after the database commit; a realtime failure never fails a request.</summary>
public interface IInboxRealtimePublisher
{
    Task MessageCreatedAsync(
        Guid tenantId, IReadOnlyCollection<Guid> recipientEmployeeIds, Guid conversationId, ConversationMessageResponse message,
        CancellationToken ct = default);

    Task ParticipantAddedAsync(
        Guid tenantId, IReadOnlyCollection<Guid> recipientEmployeeIds, Guid conversationId, ConversationParticipantResponse participant,
        CancellationToken ct = default);

    Task InboxChangedAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, ItemRef item, CancellationToken ct = default);
}
