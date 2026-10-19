using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;

namespace ONEVO.Application.Features.Conversations.Services;

/// <summary>Batched unread-message counts per inbox item, used by the inbox router to fill UnreadMessages.
/// Items the caller has no conversation for (or nothing unread in) are absent from the result.</summary>
public interface IConversationUnreadCounts
{
    Task<IReadOnlyDictionary<ItemRef, int>> GetAsync(Guid tenantId, Guid employeeId, IReadOnlyCollection<ItemRef> items, CancellationToken ct);
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
}
