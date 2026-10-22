using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Inbox.Chats;

public static class InboxChatTypes
{
    public const string Direct = "direct";
    public const string Group = "group";
    /// <summary>The dedicated group of one inbox item.</summary>
    public const string Case = "case";
}

/// <summary>A pending item in a chat that the caller can act on - what the Pending filter and bulk approval use.</summary>
public sealed record InboxChatPendingItem(string Source, string Kind, Guid Id, string Title);

/// <summary>One row of the inbox's chat list. <see cref="Source"/>/<see cref="Kind"/>/<see cref="SubjectId"/> open it
/// (POST /conversations/open). For a 1-on-1 chat <see cref="OtherEmployeeId"/> is the other person; for a case group
/// <see cref="CaseItem"/> is its item.</summary>
public sealed record InboxChatRow(
    Guid ConversationId, string Source, string Kind, Guid SubjectId, string Type, string Title,
    Guid? OtherEmployeeId, Guid? AvatarFileId, int MemberCount,
    string? Preview, DateTimeOffset? LastMessageAt, int Unread,
    IReadOnlyList<InboxChatPendingItem> Pending, InboxChatPendingItem? CaseItem, bool IsFavorite = false);

public interface IInboxChatService
{
    Task<IReadOnlyList<InboxChatRow>> ListAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct);

    /// <summary>The chat where the caller works on an item (its group when they are in it, else their 1-on-1 chat with
    /// its card), placing the card first when it is not placed yet. Null when the caller has no chat for it.</summary>
    Task<InboxChatRow?> ForItemAsync(Guid tenantId, Guid callerEmployeeId, ItemRef item, CancellationToken ct);
}

public sealed partial class InboxChatService : IInboxChatService
{
    private const int ProjectedItems = 100;
    private const int PreviewLength = 90;

    private readonly IInboxRouter _router;
    private readonly IInboxChatProjector _projector;
    private readonly IConversationRepository _conversations;
    private readonly ICallerIdentityResolver _identity;

    public InboxChatService(
        IInboxRouter router, IInboxChatProjector projector, IConversationRepository conversations, ICallerIdentityResolver identity)
    {
        _router = router;
        _projector = projector;
        _conversations = conversations;
        _identity = identity;
    }

    public async Task<IReadOnlyList<InboxChatRow>> ListAsync(Guid tenantId, Guid caller, CancellationToken ct)
    {
        var page = await _router.ListAsync(tenantId, caller, new InboxQuery(PageSize: ProjectedItems, Sort: InboxSorts.Latest), ct);
        var members = await _conversations.ListMemberConversationsAsync(tenantId, caller, ct);
        var chatOf = await _projector.ProjectAsync(tenantId, caller, page.Items, members, ct);
        // The projection may have created chats and cards: read the list again so they show.
        members = await _conversations.ListMemberConversationsAsync(tenantId, caller, ct);

        var pending = page.Items
            .Where(i => i.IsPending && i.Direction == InboxDirections.Received && i.AvailableActions.Any(a => a.Key != "open_target"))
            .Select(i => (Chat: chatOf.GetValueOrDefault(new ItemRef(i.Source, i.Kind, i.Id)), Item: new InboxChatPendingItem(i.Source, i.Kind, i.Id, i.Title)))
            .Where(p => p.Chat != Guid.Empty)
            .GroupBy(p => p.Chat)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<InboxChatPendingItem>)g.Select(p => p.Item).ToList());
        return await RowsAsync(tenantId, caller, members, pending, ct);
    }

    public async Task<InboxChatRow?> ForItemAsync(Guid tenantId, Guid caller, ItemRef item, CancellationToken ct)
    {
        var members = await _conversations.ListMemberConversationsAsync(tenantId, caller, ct);
        var chatId = await ChatOfAsync(tenantId, item, members, ct);
        if (chatId is null)
        {
            // Not placed yet (or older than the list's window): place it from the caller's own view of the item.
            var detail = await _router.GetDetailAsync(tenantId, caller, item.Source, item.Kind, item.Id, ct);
            if (!detail.IsSuccess)
                return null;
            var placed = await _projector.ProjectAsync(tenantId, caller, [detail.Value!.Item], members, ct);
            members = await _conversations.ListMemberConversationsAsync(tenantId, caller, ct);
            chatId = placed.TryGetValue(item, out var id) ? id : await ChatOfAsync(tenantId, item, members, ct);
        }
        if (chatId is null)
            return null;
        var rows = await RowsAsync(tenantId, caller, members.Where(m => m.Conversation.Id == chatId).ToList(),
            new Dictionary<Guid, IReadOnlyList<InboxChatPendingItem>>(), ct, includeUnmarkedCaseGroups: true);
        return rows.FirstOrDefault();
    }

    /// <summary>The case group when the caller is in it, else the first of the caller's chats that carries the card.</summary>
    private async Task<Guid?> ChatOfAsync(Guid tenantId, ItemRef item, IReadOnlyList<MemberConversation> members, CancellationToken ct)
    {
        var mine = members.Select(m => m.Conversation).ToList();
        var group = mine.FirstOrDefault(c => c.SubjectSource == item.Source && c.SubjectKind == item.Kind && c.SubjectId == item.Id);
        if (group is not null
            && (await _conversations.LatestSystemEventsAsync(tenantId, [group.Id], [PeopleChatEvents.CaseGroup], ct)).ContainsKey(group.Id))
            return group.Id;
        var ids = mine.Select(c => c.Id).ToHashSet();
        var card = (await _conversations.ListCaseCardsAsync(tenantId, item, ct)).FirstOrDefault(c => ids.Contains(c.ConversationId));
        return card?.ConversationId;
    }

    private async Task<IReadOnlyList<InboxChatRow>> RowsAsync(
        Guid tenantId, Guid caller, IReadOnlyList<MemberConversation> members,
        IReadOnlyDictionary<Guid, IReadOnlyList<InboxChatPendingItem>> pending, CancellationToken ct, bool includeUnmarkedCaseGroups = false)
    {
        var caseConversationIds = members.Where(m => !ConversationSources.IsPeopleChat(m.Conversation.SubjectSource)).Select(m => m.Conversation.Id).ToList();
        var groupIds = members.Where(m => m.Conversation.SubjectSource == ConversationSources.Group).Select(m => m.Conversation.Id).ToList();
        var markers = await _conversations.LatestSystemEventsAsync(tenantId, caseConversationIds, [PeopleChatEvents.CaseGroup], ct);
        var names = await _conversations.LatestSystemEventsAsync(tenantId, groupIds, PeopleChatEvents.GroupNameEvents, ct);

        // An item's own conversation is listed only once it is a group (its marker): older per-item chats stay hidden.
        var shown = members
            .Where(m => ConversationSources.IsPeopleChat(m.Conversation.SubjectSource) || markers.ContainsKey(m.Conversation.Id) || includeUnmarkedCaseGroups)
            // A 1-on-1 chat with nothing in it yet (opened, never written to) is not listed.
            .Where(m => m.Conversation.SubjectSource != ConversationSources.Direct || m.LatestMessage is not null || pending.ContainsKey(m.Conversation.Id))
            .ToList();
        var ids = shown.Select(m => m.Conversation.Id).ToList();
        var memberIds = await _conversations.ListActiveMemberIdsAsync(tenantId, ids, ct);
        // Text messages only: the badge adds the open cases (pending) on top, so a case card must not count twice.
        var unread = await _conversations.CountUnreadByConversationAsync(tenantId, caller, ids, ct, textOnly: true);
        var favorites = await _conversations.ListFavoriteConversationIdsAsync(tenantId, caller, ct);

        var others = shown.Where(m => m.Conversation.SubjectSource == ConversationSources.Direct)
            .Select(m => memberIds.GetValueOrDefault(m.Conversation.Id)?.FirstOrDefault(id => id != caller) ?? Guid.Empty)
            .Where(id => id != Guid.Empty);
        var authors = shown.Select(m => m.LatestMessage?.AuthorEmployeeId).OfType<Guid>();
        var people = await _identity.ResolveIdentitiesByEmployeeIdAsync(tenantId, others.Concat(authors).Distinct().ToList(), ct);

        return shown.Select(m =>
            {
                var c = m.Conversation;
                var count = memberIds.GetValueOrDefault(c.Id)?.Count ?? 0;
                var rowPending = pending.GetValueOrDefault(c.Id) ?? [];
                var preview = Preview(m.LatestMessage, caller, people);
                if (c.SubjectSource == ConversationSources.Direct)
                {
                    var other = memberIds.GetValueOrDefault(c.Id)?.FirstOrDefault(id => id != caller);
                    var person = other is { } o ? people.GetValueOrDefault(o) : null;
                    return new InboxChatRow(c.Id, c.SubjectSource, c.SubjectKind, c.SubjectId, InboxChatTypes.Direct, person?.Name ?? "Former colleague",
                        other, person?.AvatarFileId, count, preview, m.LatestMessage?.CreatedAt ?? c.LastMessageAt, unread.GetValueOrDefault(c.Id), rowPending, null,
                        favorites.Contains(c.Id));
                }
                if (c.SubjectSource == ConversationSources.Group)
                    return new InboxChatRow(c.Id, c.SubjectSource, c.SubjectKind, c.SubjectId, InboxChatTypes.Group, PeopleChatNames.GroupName(names.GetValueOrDefault(c.Id)),
                        null, null, count, preview, m.LatestMessage?.CreatedAt ?? c.LastMessageAt, unread.GetValueOrDefault(c.Id), rowPending, null,
                        favorites.Contains(c.Id));
                var title = MarkerTitle(markers.GetValueOrDefault(c.Id)) ?? "Case";
                return new InboxChatRow(c.Id, c.SubjectSource, c.SubjectKind, c.SubjectId, InboxChatTypes.Case, title,
                    null, null, count, preview, m.LatestMessage?.CreatedAt ?? c.LastMessageAt, unread.GetValueOrDefault(c.Id), rowPending,
                    new InboxChatPendingItem(c.SubjectSource, c.SubjectKind, c.SubjectId, title), favorites.Contains(c.Id));
            })
            .OrderByDescending(r => r.Pending.Count > 0)
            .ThenByDescending(r => r.LastMessageAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private static string? MarkerTitle(ConversationMessage? marker)
    {
        if (marker?.SystemEventJson is not { } json)
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("title", out var title) ? title.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>One line for the list: "You: …" / "Name: …" for text, the case title for a card, the line for system events.</summary>
    private static string? Preview(ConversationMessage? message, Guid caller, IReadOnlyDictionary<Guid, EmployeeIdentityDto> people)
    {
        if (message is null)
            return null;
        if (message.DeletedByEmployeeId is not null)
            return "Message deleted";
        var text = WebUtility.HtmlDecode(Tags().Replace(message.BodyHtml, " "));
        text = Spaces().Replace(text, " ").Trim();
        if (message.Kind == ConversationMessageKinds.Case)
            text = "Case: " + text;
        else if (message.Kind == ConversationMessageKinds.Text)
        {
            if (text.Length == 0)
                text = "Attachment";
            var author = message.AuthorEmployeeId == caller
                ? "You"
                : message.AuthorEmployeeId is { } id ? people.GetValueOrDefault(id)?.Name.Split(' ')[0] : null;
            if (author is not null)
                text = $"{author}: {text}";
        }
        return text.Length > PreviewLength ? text[..(PreviewLength - 1)].TrimEnd() + "…" : text;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
