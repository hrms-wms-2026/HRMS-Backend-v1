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
    /// member of) and are still in (not removed, not left) - those people can view the item and chat, so the
    /// item belongs in their Inbox.</summary>
    Task<IReadOnlyList<ItemRef>> ListAddedSubjectsAsync(Guid tenantId, Guid employeeId, string source, CancellationToken ct = default);

    /// <summary>The conversation's system messages (people added/removed, …), oldest first - the item's Steps show them.</summary>
    Task<IReadOnlyList<ConversationMessage>> ListSystemMessagesAsync(Guid tenantId, Guid conversationId, CancellationToken ct = default);

    /// <summary>The given messages of one conversation (any page), untracked - the targets of replies.</summary>
    Task<IReadOnlyList<ConversationMessage>> ListMessagesByIdAsync(
        Guid tenantId, Guid conversationId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default);

    /// <summary>Inserts and saves the message unless a row with its id exists (concurrency-safe), and keeps the row at the
    /// message's CreatedAt (its place in the chat). True when inserted.</summary>
    Task<bool> AddMessageIfMissingAsync(ConversationMessage message, CancellationToken ct = default);

    /// <summary>Tracked, for edit/delete.</summary>
    Task<ConversationMessage?> GetMessageAsync(Guid tenantId, Guid messageId, CancellationToken ct = default);
    void UpdateMessage(ConversationMessage message);

    /// <summary>Adds (does not save) mention rows for an existing message (an edit that mentions new people).</summary>
    Task AddMentionsAsync(IReadOnlyCollection<ConversationMessageMention> mentions, CancellationToken ct = default);

    /// <summary>Adds (does not save) one audit row of a message.</summary>
    Task AddRevisionAsync(ConversationMessageRevision revision, CancellationToken ct = default);
    /// <summary>Every revision of the given messages, oldest first (investigation export only).</summary>
    Task<IReadOnlyList<ConversationMessageRevision>> ListRevisionsAsync(Guid tenantId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default);

    /// <summary>Adds (does not save) a message's attachments.</summary>
    Task AddAttachmentsAsync(IReadOnlyCollection<ConversationMessageAttachment> attachments, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ConversationMessageAttachment>>> ListAttachmentsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default);
    Task<ConversationMessageAttachment?> GetAttachmentAsync(Guid tenantId, Guid attachmentId, CancellationToken ct = default);

    void Update(Conversation conversation);
    void UpdateParticipant(ConversationParticipant participant);
}
