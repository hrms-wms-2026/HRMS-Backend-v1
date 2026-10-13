using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.DTOs;

namespace ONEVO.Application.Features.Conversations.Services;

/// <summary>Used where no SignalR host is running (background workers, tests); the Api project replaces it
/// with the hub-backed publisher.</summary>
public sealed class NullInboxRealtimePublisher : IInboxRealtimePublisher
{
    public Task MessageCreatedAsync(
        Guid tenantId, IReadOnlyCollection<Guid> recipientEmployeeIds, Guid conversationId, ConversationMessageResponse message,
        CancellationToken ct = default) => Task.CompletedTask;

    public Task ParticipantAddedAsync(
        Guid tenantId, IReadOnlyCollection<Guid> recipientEmployeeIds, Guid conversationId, ConversationParticipantResponse participant,
        CancellationToken ct = default) => Task.CompletedTask;

    public Task InboxChangedAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, ItemRef item, CancellationToken ct = default)
        => Task.CompletedTask;
}
