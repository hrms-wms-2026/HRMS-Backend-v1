using ONEVO.Domain.Features.Inbox.Entities;

namespace ONEVO.Application.Features.Inbox.Revert;

/// <summary>The single "how long after a decision can it be undone" rule for every inbox kind
/// (WM's ApprovalRevertWindow reads <see cref="Minutes"/>).</summary>
public static class InboxRevertWindow
{
    public const int Minutes = 30;

    public static DateTimeOffset Until(InboxDecisionJournalEntry entry) => entry.DecidedAt.AddMinutes(Minutes);

    /// <summary>The per-row half of the standard rule: only the actor, only within the window, only once,
    /// and never a "revert" row itself. "Only the item's latest decision" needs the journal and is checked by
    /// <see cref="InboxDecisionReverterBase"/>.</summary>
    public static (bool CanRevert, DateTimeOffset? RevertableUntil) Compute(
        InboxDecisionJournalEntry entry, Guid viewerEmployeeId, DateTimeOffset now)
    {
        if (entry.Action == InboxJournalActions.Revert || entry.RevertedByJournalId is not null)
            return (false, null);
        var until = Until(entry);
        return (entry.ActorEmployeeId == viewerEmployeeId && now <= until, until);
    }
}
