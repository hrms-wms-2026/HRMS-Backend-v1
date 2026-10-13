using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.DTOs;

namespace ONEVO.Application.Features.Conversations.Services;

/// <summary>Used where no SignalR host is running (background jobs, tests); the Api project registers
/// the hub-backed publisher over this.</summary>
public sealed class NullInboxRealtimePublisher : IInboxRealtimePublisher
{
    public Task MessageCreatedAsync(Guid conversationId, ConversationMessageResponse message, CancellationToken ct = default) => Task.CompletedTask;
    public Task ParticipantAddedAsync(Guid conversationId, ConversationParticipantResponse participant, CancellationToken ct = default) => Task.CompletedTask;
    public Task InboxChangedAsync(IReadOnlyCollection<Guid> employeeIds, ItemRef item, CancellationToken ct = default) => Task.CompletedTask;
}
