namespace ONEVO.Application.Features.Conversations.DTOs;

/// <summary>JoinReason: "base" (requester/approver/subject, resolved from the item), "viewer" (sees the item from
/// their own scope), or "added" (added by another participant). Removed and departed people are not listed.</summary>
public sealed record ConversationParticipantResponse(
    Guid EmployeeId, string Name, Guid? AvatarFileId, string JoinReason, Guid? AddedByEmployeeId, DateTimeOffset? AddedAt,
    DateTimeOffset? LastReadAt = null);

public sealed record ConversationAttachmentResponse(Guid Id, string FileName, string ContentType, long SizeBytes);

/// <summary>One emoji on a message and who reacted with it (first reactor first).</summary>
public sealed record ConversationReactionResponse(string Emoji, IReadOnlyList<Guid> EmployeeIds);

/// <summary>The message a reply quotes, as it is now: an edit shows the new text, a delete shows <see cref="IsDeleted"/>.
/// <see cref="Excerpt"/> is plain text, cut short; empty for an evidence marker (the client labels it).</summary>
public sealed record ConversationReplyPreviewResponse(
    Guid Id, Guid? AuthorEmployeeId, string? AuthorName, string Kind, string Excerpt, bool IsDeleted);

/// <summary>A deleted message keeps its place with <see cref="IsDeleted"/> true, an empty body and no attachments.</summary>
public sealed record ConversationMessageResponse(
    Guid Id, Guid ConversationId, Guid? AuthorEmployeeId, string? AuthorName, Guid? AuthorAvatarFileId,
    string Kind, string BodyHtml, string? SystemEventJson, IReadOnlyList<Guid> MentionEmployeeIds, DateTimeOffset CreatedAt,
    string? Tag = null, DateTimeOffset? EditedAt = null, bool IsDeleted = false,
    IReadOnlyList<ConversationAttachmentResponse>? Attachments = null,
    IReadOnlyList<ConversationReactionResponse>? Reactions = null,
    ConversationReplyPreviewResponse? ReplyTo = null);

public sealed record ConversationMessagesPageResponse(IReadOnlyList<ConversationMessageResponse> Items, bool HasMore);

/// <summary><see cref="SubjectEmployeeId"/> is the employee an alert/case is about (only they may tag a justification).</summary>
public sealed record ConversationResponse(
    Guid Id, string Source, string Kind, Guid SubjectId, string Title, Guid? ProjectId,
    IReadOnlyList<ConversationParticipantResponse> Participants,
    IReadOnlyList<ConversationMessageResponse> Messages, bool HasMoreMessages, DateTimeOffset? LastReadAt,
    Guid? SubjectEmployeeId = null);
