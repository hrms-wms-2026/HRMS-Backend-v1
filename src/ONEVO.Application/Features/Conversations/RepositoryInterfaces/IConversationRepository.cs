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

    /// <summary>Newest first, excluding messages hidden only for <paramref name="employeeId"/>.</summary>
    Task<IReadOnlyList<ConversationMessage>> ListVisibleMessagesAsync(
        Guid tenantId, Guid conversationId, Guid employeeId, DateTimeOffset? before, int take, CancellationToken ct = default);

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
    /// <summary>Hides one message only for one participant. Repeating the request is a no-op.</summary>
    Task HideMessageForEmployeeAsync(ConversationMessageHide hide, CancellationToken ct = default);
    Task<bool> IsMessageHiddenForEmployeeAsync(Guid tenantId, Guid messageId, Guid employeeId, CancellationToken ct = default);
    /// <summary>Every revision of the given messages, oldest first (investigation export only).</summary>
    Task<IReadOnlyList<ConversationMessageRevision>> ListRevisionsAsync(Guid tenantId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default);

    /// <summary>Adds (does not save) a message's attachments.</summary>
    Task AddAttachmentsAsync(IReadOnlyCollection<ConversationMessageAttachment> attachments, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ConversationMessageAttachment>>> ListAttachmentsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default);
    Task<ConversationMessageAttachment?> GetAttachmentAsync(Guid tenantId, Guid attachmentId, CancellationToken ct = default);

    void Update(Conversation conversation);
    void UpdateParticipant(ConversationParticipant participant);

    // ---- People chats (the inbox's chat list) -------------------------------------------------------------

    /// <summary>Every conversation the employee is an active member of (has a row, not removed, not left), with their
    /// read marker and the conversation's latest message (null when it has none).</summary>
    Task<IReadOnlyList<MemberConversation>> ListMemberConversationsAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Per conversation: messages not written by the employee and newer than their LastReadAt. <paramref name="textOnly"/>
    /// counts only people's text messages (the chat list adds the open cases itself, so case cards would count twice).</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountUnreadByConversationAsync(
        Guid tenantId, Guid employeeId, IReadOnlyCollection<Guid> conversationIds, CancellationToken ct = default, bool textOnly = false);

    /// <summary>Per conversation: its active members' employee ids (rows only - not the subject's live base people).</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ListActiveMemberIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> conversationIds, CancellationToken ct = default);

    /// <summary>Which of the given message ids exist (the deterministic ids of case cards and their lines).</summary>
    Task<IReadOnlySet<Guid>> ListExistingMessageIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default);

    /// <summary>The case cards placed for one inbox item, in any conversation (oldest first). Forwarded copies (cards with an
    /// author) are not included.</summary>
    Task<IReadOnlyList<ConversationMessage>> ListCaseCardsAsync(Guid tenantId, ItemRef item, CancellationToken ct = default);

    /// <summary>Per conversation: its latest system message whose event type is one of <paramref name="eventTypes"/>
    /// (a group's current name, a case group's marker).</summary>
    Task<IReadOnlyDictionary<Guid, ConversationMessage>> LatestSystemEventsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> conversationIds, IReadOnlyCollection<string> eventTypes, CancellationToken ct = default);

    /// <summary>The chats the employee pinned (their favorites).</summary>
    Task<IReadOnlySet<Guid>> ListFavoriteConversationIdsAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Pins (or unpins) a chat for the employee and saves; repeating either is a no-op.</summary>
    Task SetFavoriteAsync(Guid tenantId, Guid employeeId, Guid conversationId, bool favorite, CancellationToken ct = default);

    /// <summary>Removes a system-written message (a case's "resolved" line after an undo) when it exists.</summary>
    Task DeleteMessageIfExistsAsync(Guid tenantId, Guid messageId, CancellationToken ct = default);

    /// <summary>Moves LastMessageAt forward to <paramref name="at"/> (never back) and saves.</summary>
    Task TouchAsync(Guid tenantId, Guid conversationId, DateTimeOffset at, CancellationToken ct = default);
}

/// <summary>One of an employee's conversations for the chat list.</summary>
public sealed record MemberConversation(Conversation Conversation, DateTimeOffset? LastReadAt, ConversationMessage? LatestMessage);
