using System.Net;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Domain.Features.Inbox.Entities;

namespace ONEVO.Application.Features.Inbox.Services;

/// <summary>One timeline per item: the provider's module steps, the decision journal and the conversation's
/// system events (people added/removed), merged in time order.</summary>
public interface IInboxStepsBuilder
{
    Task<IReadOnlyList<InboxStep>> BuildAsync(Guid tenantId, ItemRef item, IReadOnlyList<InboxStep> moduleSteps, CancellationToken ct);
}

public sealed class InboxStepsBuilder : IInboxStepsBuilder
{
    /// <summary>A journal row this close to a module step by the same actor is the same decision.</summary>
    public static readonly TimeSpan SameDecisionTolerance = TimeSpan.FromMinutes(2);

    private readonly IInboxDecisionJournal _journal;
    private readonly IConversationRepository _conversations;
    private readonly ICallerIdentityResolver _identity;

    public InboxStepsBuilder(IInboxDecisionJournal journal, IConversationRepository conversations, ICallerIdentityResolver identity)
    {
        _journal = journal;
        _conversations = conversations;
        _identity = identity;
    }

    public async Task<IReadOnlyList<InboxStep>> BuildAsync(
        Guid tenantId, ItemRef item, IReadOnlyList<InboxStep> moduleSteps, CancellationToken ct)
    {
        var journal = await _journal.ListForItemAsync(tenantId, item, ct);
        var conversation = await _conversations.GetBySubjectAsync(tenantId, item, ct);
        var events = conversation is null
            ? []
            : await _conversations.ListSystemMessagesAsync(tenantId, conversation.Id, ct);
        if (journal.Count == 0 && events.Count == 0)
            return moduleSteps;

        var actorIds = journal.Select(j => j.ActorEmployeeId).Distinct().ToList();
        var names = (await _identity.ResolveIdentitiesByEmployeeIdAsync(tenantId, actorIds, ct))
            .ToDictionary(p => p.Key, p => p.Value.Name);
        return Merge(moduleSteps, journal, events, names);
    }

    /// <summary>Dated steps in time order (stable), then undated ones (current/upcoming) in the provider's order.
    /// A journal row that matches a module step (same actor, within <see cref="SameDecisionTolerance"/>) is folded
    /// into it rather than shown twice; a reverted decision is shown with state "reverted".</summary>
    public static IReadOnlyList<InboxStep> Merge(
        IReadOnlyList<InboxStep> moduleSteps, IReadOnlyList<InboxDecisionJournalEntry> journal,
        IReadOnlyList<ConversationMessage> systemEvents, IReadOnlyDictionary<Guid, string> names)
    {
        var steps = moduleSteps.ToList();
        var extra = new List<InboxStep>();
        foreach (var entry in journal)
        {
            var reverted = entry.RevertedByJournalId is not null;
            var match = entry.Action == InboxJournalActions.Revert ? -1 : steps.FindIndex(s =>
                s.Actor?.Id == entry.ActorEmployeeId && s.At is { } at && (at - entry.DecidedAt).Duration() <= SameDecisionTolerance);
            if (match >= 0)
            {
                if (reverted)
                    steps[match] = steps[match] with { State = InboxStepStates.Reverted };
                continue;
            }
            extra.Add(new InboxStep($"journal:{entry.Id}", JournalLabel(entry.Action),
                new InboxPerson(entry.ActorEmployeeId, names.GetValueOrDefault(entry.ActorEmployeeId, "A teammate")),
                entry.DecidedAt, reverted ? InboxStepStates.Reverted : InboxStepStates.Done, entry.Comment));
        }
        extra.AddRange(systemEvents.Select(m => new InboxStep(
            $"conversation:{m.Id}", WebUtility.HtmlDecode(m.BodyHtml), null, m.CreatedAt, InboxStepStates.Done, null)));

        var dated = steps.Concat(extra).Where(s => s.At is not null).OrderBy(s => s.At).ToList();
        return dated.Concat(steps.Where(s => s.At is null)).ToList();
    }

    public static string JournalLabel(string action) => action switch
    {
        InboxJournalActions.Approve => "Approved",
        InboxJournalActions.Reject => "Rejected",
        InboxJournalActions.Acknowledge => "Acknowledged",
        InboxJournalActions.Escalate => "Escalated",
        InboxJournalActions.Cancel => "Cancelled",
        InboxJournalActions.Revert => "Decision undone",
        _ when action.StartsWith(InboxJournalActions.ResolvePrefix, StringComparison.Ordinal)
            => "Resolved - " + Humanize(action[InboxJournalActions.ResolvePrefix.Length..]),
        _ => Humanize(action),
    };

    private static string Humanize(string value)
    {
        var text = value.Replace('_', ' ');
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
