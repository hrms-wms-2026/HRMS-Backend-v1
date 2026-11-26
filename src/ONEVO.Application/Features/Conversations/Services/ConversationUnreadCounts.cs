using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;

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

    /// <summary>Event JSON is written with "type" first ({"type":"case_group",...}), so this prefix finds a case group.</summary>
    public const string CaseGroupEventPrefix = "{\"type\":\"" + PeopleChatEvents.CaseGroup + "\"";

    // One aggregate (Task 00a): listing every chat with its latest message and every case's system events only to
    // add up a number was the slowest step of the inbox badge. Muted chats do not count towards it.
    public Task<int> TotalInChatsAsync(Guid tenantId, Guid employeeId, CancellationToken ct)
        => _conversations.CountUnreadInListedChatsAsync(tenantId, employeeId, CaseGroupEventPrefix, ct);
}
