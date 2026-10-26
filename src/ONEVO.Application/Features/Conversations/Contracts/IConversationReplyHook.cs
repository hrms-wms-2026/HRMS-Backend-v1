namespace ONEVO.Application.Features.Conversations.Contracts;

/// <summary>A module's reaction to someone posting in an item's chat (e.g. a reviewer replying on an open case
/// acknowledges it). Runs after the message is saved; a hook that does nothing for the item returns null.</summary>
public interface IConversationReplyHook
{
    /// <summary>The employees whose Inbox changed because of the reply, or null when the item was left as it was.</summary>
    Task<IReadOnlyCollection<Guid>?> OnReplyAsync(Guid tenantId, ItemRef subject, Guid authorEmployeeId, CancellationToken ct);
}
