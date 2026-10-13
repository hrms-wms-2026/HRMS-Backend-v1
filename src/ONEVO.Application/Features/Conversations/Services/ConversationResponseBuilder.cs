using ONEVO.Application.Features.Conversations.DTOs;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Conversations.Services;

/// <summary>Maps conversation rows to responses, resolving names and avatars in one batch per call.</summary>
public sealed class ConversationResponseBuilder
{
    private const string FallbackName = "A teammate";

    private readonly ICallerIdentityResolver _identity;
    private readonly IConversationRepository _conversations;

    public ConversationResponseBuilder(ICallerIdentityResolver identity, IConversationRepository conversations)
    {
        _identity = identity;
        _conversations = conversations;
    }

    public async Task<IReadOnlyList<ConversationParticipantResponse>> ParticipantsAsync(
        Guid tenantId, ConversationContext context, CancellationToken ct)
    {
        var people = await _identity.ResolveIdentitiesByEmployeeIdAsync(tenantId, context.ParticipantIds, ct);
        return context.ParticipantIds.Select(id =>
        {
            var row = context.RowOf(id);
            var reason = context.IsBase(id) ? ConversationJoinReasons.Base : ConversationJoinReasons.Added;
            return Participant(id, people.GetValueOrDefault(id), reason,
                reason == ConversationJoinReasons.Added ? row?.AddedByEmployeeId : null, row?.AddedAt);
        }).ToList();
    }

    public static ConversationParticipantResponse Participant(
        Guid employeeId, EmployeeIdentityDto? identity, string joinReason, Guid? addedBy, DateTimeOffset? addedAt)
        => new(employeeId, identity?.Name ?? FallbackName, identity?.AvatarFileId, joinReason, addedBy, addedAt);

    /// <summary>Messages in the order given, with their mentions and authors.</summary>
    public async Task<IReadOnlyList<ConversationMessageResponse>> MessagesAsync(
        Guid tenantId, IReadOnlyList<ConversationMessage> messages, CancellationToken ct)
    {
        if (messages.Count == 0)
            return [];
        var mentions = await _conversations.ListMentionsAsync(tenantId, messages.Select(m => m.Id).ToList(), ct);
        var authorIds = messages.Where(m => m.AuthorEmployeeId.HasValue).Select(m => m.AuthorEmployeeId!.Value).Distinct().ToList();
        var authors = authorIds.Count == 0
            ? new Dictionary<Guid, EmployeeIdentityDto>()
            : await _identity.ResolveIdentitiesByEmployeeIdAsync(tenantId, authorIds, ct);
        return messages.Select(m => Message(m, mentions.GetValueOrDefault(m.Id) ?? [],
            m.AuthorEmployeeId is { } author ? authors.GetValueOrDefault(author) : null)).ToList();
    }

    public static ConversationMessageResponse Message(
        ConversationMessage message, IReadOnlyList<Guid> mentionEmployeeIds, EmployeeIdentityDto? author)
        => new(message.Id, message.ConversationId, message.AuthorEmployeeId,
            message.AuthorEmployeeId.HasValue ? author?.Name ?? FallbackName : null, author?.AvatarFileId,
            message.Kind, message.BodyHtml, message.SystemEventJson, mentionEmployeeIds, message.CreatedAt);
}
