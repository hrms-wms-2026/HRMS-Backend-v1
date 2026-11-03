using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Conversations.Services;

/// <summary>Batched unread-message counts per inbox item, used by the inbox router to fill UnreadMessages.
/// Items the caller has no conversation for (or nothing unread in) are absent from the result.</summary>
public interface IConversationUnreadCounts
{
    Task<IReadOnlyDictionary<ItemRef, int>> GetAsync(Guid tenantId, Guid employeeId, IReadOnlyCollection<ItemRef> items, CancellationToken ct);

    /// <summary>Unread messages across the chats the employee's inbox lists - 1-on-1 chats, groups and case groups
    /// (an item's own conversation counts only once it is a group; older per-item chats are hidden, so they don't).</summary>
    Task<int> TotalInChatsAsync(Guid tenantId, Guid employeeId, CancellationToken ct);
}

public sealed class ConversationUnreadCounts : IConversationUnreadCounts
{
    private readonly IConversationRepository _conversations;

    public ConversationUnreadCounts(IConversationRepository conversations) => _conversations = conversations;

    public async Task<IReadOnlyDictionary<ItemRef, int>> GetAsync(
        Guid tenantId, Guid employeeId, IReadOnlyCollection<ItemRef> items, CancellationToken ct)
        => items.Count == 0
            ? new Dictionary<ItemRef, int>()
            : await _conversations.CountUnreadAsync(tenantId, employeeId, items, ct);

    public async Task<int> TotalInChatsAsync(Guid tenantId, Guid employeeId, CancellationToken ct)
    {
        var members = await _conversations.ListMemberConversationsAsync(tenantId, employeeId, ct);
        var caseIds = members.Where(m => !ConversationSources.IsPeopleChat(m.Conversation.SubjectSource)).Select(m => m.Conversation.Id).ToList();
        var groups = await _conversations.LatestSystemEventsAsync(tenantId, caseIds, [PeopleChatEvents.CaseGroup], ct);
        // A muted chat's messages do not count towards the inbox badge.
        var muted = await _conversations.ListMutedConversationIdsAsync(tenantId, employeeId, ct);
        var listed = members
            .Where(m => ConversationSources.IsPeopleChat(m.Conversation.SubjectSource) || groups.ContainsKey(m.Conversation.Id))
            .Where(m => !muted.Contains(m.Conversation.Id))
            .Select(m => m.Conversation.Id)
            .ToList();
        var unread = await _conversations.CountUnreadByConversationAsync(tenantId, employeeId, listed, ct);
        return unread.Values.Sum();
    }
}
