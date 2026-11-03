namespace ONEVO.Application.Features.Conversations.Contracts;

/// <summary>What someone did in an item's conversation, for modules that react to chat activity.</summary>
public static class ConversationActivities
{
    public const string MessagePosted = "message_posted";
    public const string MemberAdded = "member_added";
    public const string MemberRemoved = "member_removed";
    public const string Reacted = "reacted";
}

/// <summary>
/// Told after a chat activity has been saved (a message posted, people added or removed, a reaction). A module uses it
/// to treat the activity as attention to its item - e.g. an exception case's reviewer replying acknowledges the case.
/// Runs after the chat change committed and is best-effort: a listener failing never fails the chat request.
/// </summary>
public interface IConversationActivityListener
{
    Task OnActivityAsync(Guid tenantId, ItemRef subject, Guid actorEmployeeId, string activity, CancellationToken ct);
}
