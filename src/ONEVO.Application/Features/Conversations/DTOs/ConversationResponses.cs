namespace ONEVO.Application.Features.Conversations.DTOs;

public sealed record ConversationParticipantResponse(
    Guid EmployeeId, string Name, Guid? AvatarFileId, string JoinReason, Guid? AddedByEmployeeId, DateTimeOffset? AddedAt);

public sealed record ConversationMessageResponse(
    Guid Id, Guid ConversationId, Guid? AuthorEmployeeId, string? AuthorName, Guid? AuthorAvatarFileId,
    string Kind, string BodyHtml, string? SystemEventJson, IReadOnlyList<Guid> MentionEmployeeIds, DateTimeOffset CreatedAt);

public sealed record ConversationMessagesPageResponse(IReadOnlyList<ConversationMessageResponse> Items, bool HasMore);

public sealed record ConversationResponse(
    Guid Id, string Source, string Kind, Guid SubjectId, string Title, Guid? ProjectId,
    IReadOnlyList<ConversationParticipantResponse> Participants,
    IReadOnlyList<ConversationMessageResponse> Messages, bool HasMoreMessages, DateTimeOffset? LastReadAt);
