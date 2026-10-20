using System.Net;
using System.Text.Json;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Inbox.Chats;

/// <summary>
/// Puts the caller's inbox items into people chats, from the caller's own view of each item (their inbox list - the
/// modules' permission checks stay theirs). Run on every chat-list load; each write has a deterministic id, so the
/// requester and the reviewer projecting the same item agree and repeats change nothing.
/// <list type="bullet">
/// <item>A new item: a case card in the 1-on-1 chat of the person it is about/from and their reviewer.</item>
/// <item>A pending item that reaches a reviewer after its card is already in someone else's chat (the next approver of a
/// multi-step request, a delegate): the case moves to its group and they join it as the next approver.</item>
/// <item>A decided or closed item: a "resolved" line under its card (in the group too, when it has one).</item>
/// </list>
/// </summary>
public interface IInboxChatProjector
{
    /// <summary>Returns, per item, the conversation where the caller works on it (their 1-on-1 chat or its group).</summary>
    Task<IReadOnlyDictionary<ItemRef, Guid>> ProjectAsync(
        Guid tenantId, Guid callerEmployeeId, IReadOnlyList<InboxItem> items, IReadOnlyList<MemberConversation> memberConversations,
        CancellationToken ct);
}

public sealed class InboxChatProjector : IInboxChatProjector
{
    private readonly IConversationRepository _conversations;
    private readonly IPeopleChatService _people;
    private readonly ICaseGroupService _caseGroups;

    public InboxChatProjector(IConversationRepository conversations, IPeopleChatService people, ICaseGroupService caseGroups)
    {
        _conversations = conversations;
        _people = people;
        _caseGroups = caseGroups;
    }

    public async Task<IReadOnlyDictionary<ItemRef, Guid>> ProjectAsync(
        Guid tenantId, Guid caller, IReadOnlyList<InboxItem> items, IReadOnlyList<MemberConversation> memberConversations, CancellationToken ct)
    {
        var result = new Dictionary<ItemRef, Guid>();
        var directByPair = memberConversations
            .Where(m => m.Conversation.SubjectSource == ConversationSources.Direct)
            .ToDictionary(m => m.Conversation.SubjectId, m => m.Conversation);
        var groupByItem = memberConversations
            .Where(m => !ConversationSources.IsPeopleChat(m.Conversation.SubjectSource))
            .ToDictionary(m => new ItemRef(m.Conversation.SubjectSource, m.Conversation.SubjectKind, m.Conversation.SubjectId), m => m.Conversation);
        var groupMarkers = await _conversations.LatestSystemEventsAsync(
            tenantId, groupByItem.Values.Select(c => c.Id).ToList(), [PeopleChatEvents.CaseGroup], ct);

        var candidates = new List<Candidate>();
        foreach (var item in items)
        {
            if (ConversationSources.IsPeopleChat(item.Source) || item.RequestedBy?.Id is not { } person)
                continue;
            var itemRef = new ItemRef(item.Source, item.Kind, item.Id);
            if (person == caller)
            {
                // The requester's side: the chat with whoever holds it now. Alerts about them have no holder here -
                // the reviewer's side places those.
                if (item.RequestedTo?.Id is { } holder && holder != caller)
                    candidates.Add(new Candidate(item, itemRef, holder, IsHolder: false));
            }
            else if (item.Direction == InboxDirections.Received)
            {
                candidates.Add(new Candidate(item, itemRef, person, IsHolder: true));
            }
        }

        var ids = candidates.SelectMany(c =>
        {
            var pair = PeopleChatNames.DirectSubjectId(caller, c.Other);
            var group = groupByItem.GetValueOrDefault(c.Ref);
            return group is null
                ? new[] { CaseCards.CardId(pair, c.Ref), CaseCards.ResolvedId(pair, c.Ref) }
                : [CaseCards.CardId(pair, c.Ref), CaseCards.ResolvedId(pair, c.Ref), CaseCards.ResolvedId(group.Id, c.Ref)];
        }).ToList();
        var existing = await _conversations.ListExistingMessageIdsAsync(tenantId, ids, ct);

        foreach (var c in candidates)
        {
            var pair = PeopleChatNames.DirectSubjectId(caller, c.Other);
            var cardId = CaseCards.CardId(pair, c.Ref);
            var closed = CaseCards.IsClosed(c.Item.Status);

            // In the case's group already: the group is where it is worked on, and where it is closed.
            if (groupByItem.TryGetValue(c.Ref, out var group) && groupMarkers.ContainsKey(group.Id))
            {
                result[c.Ref] = group.Id;
                if (!closed && existing.Contains(CaseCards.ResolvedId(group.Id, c.Ref)))
                    await ReopenAsync(tenantId, c.Ref, CaseCards.ResolvedId(group.Id, c.Ref), ct);
                if (closed && !existing.Contains(CaseCards.ResolvedId(group.Id, c.Ref)))
                {
                    await AddResolvedAsync(tenantId, group.Id, CaseCards.ResolvedId(group.Id, c.Ref), c.Item, ct);
                    // The 1-on-1 chats that carried the card get the line too, under their "Continued in a group".
                    foreach (var card in await _conversations.ListCaseCardsAsync(tenantId, c.Ref, ct))
                    {
                        if (card.ConversationId == group.Id)
                            continue;
                        var chat = await _conversations.GetByIdAsync(tenantId, card.ConversationId, ct);
                        if (chat?.SubjectSource == ConversationSources.Direct)
                            await AddResolvedAsync(tenantId, chat.Id, CaseCards.ResolvedId(chat.SubjectId, c.Ref), c.Item, ct);
                    }
                }
                continue;
            }

            if (existing.Contains(cardId))
            {
                var chat = directByPair.GetValueOrDefault(pair) ?? await _people.GetOrCreateDirectAsync(tenantId, caller, c.Other, ct);
                result[c.Ref] = chat.Id;
                if (!closed && existing.Contains(CaseCards.ResolvedId(pair, c.Ref)))
                    await ReopenAsync(tenantId, c.Ref, CaseCards.ResolvedId(pair, c.Ref), ct);
                if (closed && !existing.Contains(CaseCards.ResolvedId(pair, c.Ref)))
                    await AddResolvedAsync(tenantId, chat.Id, CaseCards.ResolvedId(pair, c.Ref), c.Item, ct);
                continue;
            }

            if (c.Item.IsPending)
            {
                var cards = await _conversations.ListCaseCardsAsync(tenantId, c.Ref, ct);
                if (cards.Count > 0)
                {
                    // Already in someone else's chat. A reviewer reaching it now is a later approver: the case moves to
                    // its group and they join it. The requester's side has nothing to add - the card is in their chat.
                    if (c.IsHolder && await _caseGroups.EnsureAsync(tenantId, c.Ref, null, ct) is { } caseGroup)
                    {
                        await _caseGroups.AddNextApproverAsync(tenantId, caseGroup, caller, ct);
                        result[c.Ref] = caseGroup.Id;
                    }
                    continue;
                }
            }

            var direct = directByPair.GetValueOrDefault(pair) ?? await _people.GetOrCreateDirectAsync(tenantId, caller, c.Other, ct);
            directByPair[pair] = direct;
            await _conversations.AddMessageIfMissingAsync(new ConversationMessage
            {
                Id = cardId, TenantId = tenantId, ConversationId = direct.Id, Kind = ConversationMessageKinds.Case,
                BodyHtml = WebUtility.HtmlEncode(c.Item.Title), SystemEventJson = CaseCards.CardJson(c.Ref, c.Item.Title),
                CreatedAt = c.Item.CreatedAt,
            }, ct);
            await _conversations.TouchAsync(tenantId, direct.Id, c.Item.CreatedAt, ct);
            if (closed)
                await AddResolvedAsync(tenantId, direct.Id, CaseCards.ResolvedId(pair, c.Ref), c.Item, ct);
            result[c.Ref] = direct.Id;
        }
        return result;
    }

    /// <summary>"Case resolved" (alerts, cases) or "Request approved/rejected/..." (requests), placed at the item's last
    /// change - its decision.</summary>
    private async Task AddResolvedAsync(Guid tenantId, Guid conversationId, Guid lineId, InboxItem item, CancellationToken ct)
    {
        var at = item.UpdatedAt < item.CreatedAt ? item.CreatedAt : item.UpdatedAt;
        var body = item.Category == InboxCategories.Request ? $"Request {Humanize(item.Status)}" : "Case resolved";
        await _conversations.AddMessageIfMissingAsync(new ConversationMessage
        {
            Id = lineId, TenantId = tenantId, ConversationId = conversationId, Kind = ConversationMessageKinds.System,
            BodyHtml = WebUtility.HtmlEncode(body),
            SystemEventJson = JsonSerializer.Serialize(new
            {
                type = PeopleChatEvents.CaseResolved, source = item.Source, kind = item.Kind, id = item.Id, title = item.Title,
                status = item.Status, category = item.Category,
            }),
            CreatedAt = at.AddTicks(1),
        }, ct);
        await _conversations.TouchAsync(tenantId, conversationId, at.AddTicks(1), ct);
    }

    /// <summary>A decision was undone (the case is open again): its "resolved" lines go, in the group and in every
    /// 1-on-1 chat that carries its card.</summary>
    private async Task ReopenAsync(Guid tenantId, ItemRef item, Guid knownLineId, CancellationToken ct)
    {
        await _conversations.DeleteMessageIfExistsAsync(tenantId, knownLineId, ct);
        foreach (var card in await _conversations.ListCaseCardsAsync(tenantId, item, ct))
        {
            var chat = await _conversations.GetByIdAsync(tenantId, card.ConversationId, ct);
            if (chat is null)
                continue;
            var key = chat.SubjectSource == ConversationSources.Direct ? chat.SubjectId : chat.Id;
            await _conversations.DeleteMessageIfExistsAsync(tenantId, CaseCards.ResolvedId(key, item), ct);
        }
    }

    private static string Humanize(string status) => status.Replace('_', ' ').ToLowerInvariant();

    private sealed record Candidate(InboxItem Item, ItemRef Ref, Guid Other, bool IsHolder);
}
