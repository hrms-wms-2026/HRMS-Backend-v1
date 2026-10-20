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
            var reason = context.JoinReasonOf(id);
            return Participant(id, people.GetValueOrDefault(id), reason,
                reason == ConversationJoinReasons.Base ? null : row?.AddedByEmployeeId, row?.AddedAt);
        }).ToList();
    }

    public static ConversationParticipantResponse Participant(
        Guid employeeId, EmployeeIdentityDto? identity, string joinReason, Guid? addedBy, DateTimeOffset? addedAt)
        => new(employeeId, identity?.Name ?? FallbackName, identity?.AvatarFileId, joinReason, addedBy, addedAt);

    /// <summary>Messages in the order given, with their mentions, attachments, authors and the messages they reply to
    /// (fetched by id, as a quoted message may be outside the loaded page).</summary>
    public async Task<IReadOnlyList<ConversationMessageResponse>> MessagesAsync(
        Guid tenantId, IReadOnlyList<ConversationMessage> messages, CancellationToken ct)
    {
        if (messages.Count == 0)
            return [];
        var ids = messages.Select(m => m.Id).ToList();
        var mentions = await _conversations.ListMentionsAsync(tenantId, ids, ct);
        var attachments = await _conversations.ListAttachmentsAsync(tenantId, ids, ct);
        var reactions = await _conversations.ListReactionsAsync(tenantId, ids, ct);
        var quoted = await QuotedAsync(tenantId, messages, ct);
        var authorIds = messages.Concat(quoted.Values)
            .Where(m => m.AuthorEmployeeId.HasValue).Select(m => m.AuthorEmployeeId!.Value).Distinct().ToList();
        var authors = authorIds.Count == 0
            ? new Dictionary<Guid, EmployeeIdentityDto>()
            : await _identity.ResolveIdentitiesByEmployeeIdAsync(tenantId, authorIds, ct);
        return messages.Select(m => Message(m, mentions.GetValueOrDefault(m.Id) ?? [],
            m.AuthorEmployeeId is { } author ? authors.GetValueOrDefault(author) : null,
            attachments.GetValueOrDefault(m.Id), reactions.GetValueOrDefault(m.Id),
            m.ReplyToMessageId is { } target && quoted.TryGetValue(target, out var q) ? ReplyPreview(q, authors) : null)).ToList();
    }

    /// <summary>The quote of the message <paramref name="message"/> replies to, or null when it is not a reply.</summary>
    public async Task<ConversationReplyPreviewResponse?> ReplyPreviewAsync(Guid tenantId, ConversationMessage message, CancellationToken ct)
    {
        var quoted = await QuotedAsync(tenantId, [message], ct);
        if (message.ReplyToMessageId is not { } target || !quoted.TryGetValue(target, out var q))
            return null;
        var authors = q.AuthorEmployeeId is { } author
            ? await _identity.ResolveIdentitiesByEmployeeIdAsync(tenantId, [author], ct)
            : new Dictionary<Guid, EmployeeIdentityDto>();
        return ReplyPreview(q, authors);
    }

    /// <summary>A deleted message is returned as a placeholder: no body, mentions, attachments or reactions.</summary>
    public static ConversationMessageResponse Message(
        ConversationMessage message, IReadOnlyList<Guid> mentionEmployeeIds, EmployeeIdentityDto? author,
        IReadOnlyList<ConversationMessageAttachment>? attachments = null, IReadOnlyList<ConversationMessageReaction>? reactions = null,
        ConversationReplyPreviewResponse? replyTo = null)
    {
        var deleted = message.IsRetracted;
        return new(message.Id, message.ConversationId, message.AuthorEmployeeId,
            message.AuthorEmployeeId.HasValue ? author?.Name ?? FallbackName : null, author?.AvatarFileId,
            message.Kind, deleted ? string.Empty : message.BodyHtml, message.SystemEventJson,
            deleted ? [] : mentionEmployeeIds, message.CreatedAt,
            message.Tag, message.EditedAt, deleted,
            deleted || attachments is null
                ? []
                : attachments.Select(a => new ConversationAttachmentResponse(a.Id, a.FileName, a.ContentType, a.SizeBytes)).ToList(),
            deleted || reactions is null ? [] : Reactions(reactions),
            deleted ? null : replyTo);
    }

    private async Task<IReadOnlyDictionary<Guid, ConversationMessage>> QuotedAsync(
        Guid tenantId, IReadOnlyList<ConversationMessage> messages, CancellationToken ct)
    {
        var targets = messages.Where(m => m.ReplyToMessageId.HasValue)
            .GroupBy(m => m.ConversationId)
            .ToList();
        var result = new Dictionary<Guid, ConversationMessage>();
        foreach (var group in targets)
        {
            var rows = await _conversations.ListMessagesByIdAsync(
                tenantId, group.Key, group.Select(m => m.ReplyToMessageId!.Value).Distinct().ToList(), ct);
            foreach (var row in rows)
                result[row.Id] = row;
        }
        return result;
    }

    private static ConversationReplyPreviewResponse ReplyPreview(
        ConversationMessage quoted, IReadOnlyDictionary<Guid, EmployeeIdentityDto> authors)
    {
        var deleted = quoted.IsRetracted;
        var name = quoted.AuthorEmployeeId is { } author ? authors.GetValueOrDefault(author)?.Name ?? FallbackName : null;
        var excerpt = deleted || quoted.Kind == ConversationMessageKinds.Evidence ? string.Empty : ConversationRules.Excerpt(quoted.BodyHtml);
        return new ConversationReplyPreviewResponse(quoted.Id, quoted.AuthorEmployeeId, name, quoted.Kind, excerpt, deleted);
    }

    /// <summary>Grouped by emoji, in the order each emoji was first used (a replaced reaction counts from when it changed).</summary>
    public static IReadOnlyList<ConversationReactionResponse> Reactions(IReadOnlyList<ConversationMessageReaction> reactions)
        => reactions.OrderBy(ReactedAt)
            .GroupBy(r => r.Emoji)
            .Select(g => new ConversationReactionResponse(g.Key, g.Select(r => r.EmployeeId).ToList()))
            .ToList();

    /// <summary>When the person reacted with their current emoji.</summary>
    public static DateTimeOffset ReactedAt(ConversationMessageReaction reaction) => reaction.UpdatedAt ?? reaction.CreatedAt;
}
