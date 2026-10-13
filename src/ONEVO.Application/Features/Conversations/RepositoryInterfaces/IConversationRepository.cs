using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Conversations.RepositoryInterfaces;

public interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<Conversation?> GetBySubjectAsync(Guid tenantId, ItemRef subject, CancellationToken ct = default);

    /// <summary>Inserts and saves <paramref name="conversation"/>; when a concurrent request already created
    /// the conversation for the same subject (unique index), returns that one instead.</summary>
    Task<Conversation> GetOrCreateAsync(Conversation conversation, CancellationToken ct = default);

    Task<IReadOnlyList<ConversationParticipant>> ListParticipantsAsync(Guid tenantId, Guid conversationId, CancellationToken ct = default);

    /// <summary>Inserts and saves the row unless that employee already has one (concurrency-safe). True when inserted.</summary>
    Task<bool> AddParticipantIfMissingAsync(ConversationParticipant participant, CancellationToken ct = default);

    Task AddMessageAsync(ConversationMessage message, IReadOnlyCollection<ConversationMessageMention> mentions, CancellationToken ct = default);

    /// <summary>Newest first; at most <paramref name="take"/> messages created before <paramref name="before"/>.</summary>
    Task<IReadOnlyList<ConversationMessage>> ListMessagesAsync(
        Guid tenantId, Guid conversationId, DateTimeOffset? before, int take, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ListMentionsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default);

    /// <summary>Per subject: messages not written by <paramref name="employeeId"/> and newer than that
    /// employee's LastReadAt, for conversations the employee has a participant row in. Subjects with no
    /// conversation, no row for the employee, or nothing unread are absent.</summary>
    Task<IReadOnlyDictionary<ItemRef, int>> CountUnreadAsync(
        Guid tenantId, Guid employeeId, IReadOnlyCollection<ItemRef> subjects, CancellationToken ct = default);

    /// <summary>Items of <paramref name="source"/> whose conversation the employee was added to (not a base
    /// member of) - those people can view the item and chat, so the item belongs in their Inbox.</summary>
    Task<IReadOnlyList<ItemRef>> ListAddedSubjectsAsync(Guid tenantId, Guid employeeId, string source, CancellationToken ct = default);

    void Update(Conversation conversation);
    void UpdateParticipant(ConversationParticipant participant);
}
