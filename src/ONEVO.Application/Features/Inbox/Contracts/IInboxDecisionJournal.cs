using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Domain.Features.Inbox.Entities;

namespace ONEVO.Application.Features.Inbox.Contracts;

/// <summary>The inbox decision journal (<c>inbox_decision_journal</c>). Writes save immediately, so a provider
/// that must land the module change and the journal row together wraps both in
/// <c>IUnitOfWork.ExecuteInTransactionAsync</c>.</summary>
public interface IInboxDecisionJournal
{
    Task RecordAsync(InboxDecisionJournalEntry entry, CancellationToken ct = default);

    /// <summary>The item's most recent entry (any action, including "revert"), or null.</summary>
    Task<InboxDecisionJournalEntry?> LatestAsync(Guid tenantId, ItemRef item, CancellationToken ct = default);

    /// <summary>Oldest first.</summary>
    Task<IReadOnlyList<InboxDecisionJournalEntry>> ListForItemAsync(Guid tenantId, ItemRef item, CancellationToken ct = default);

    /// <summary>Newest first, decided within [from, to] when given.</summary>
    Task<InboxJournalPage> ListByActorAsync(
        Guid tenantId, Guid actorEmployeeId, DateTimeOffset? from, DateTimeOffset? to, string? source, string? kind,
        int page, int pageSize, CancellationToken ct = default);

    /// <summary>Per item of the source and kind, its newest escalation that was not undone (who holds the item now).</summary>
    Task<IReadOnlyList<InboxDecisionJournalEntry>> ListLatestEscalationsAsync(
        Guid tenantId, string source, string kind, CancellationToken ct = default);

    /// <summary>Records <paramref name="revertEntry"/> and points <paramref name="original"/> at it.</summary>
    Task MarkRevertedAsync(InboxDecisionJournalEntry original, InboxDecisionJournalEntry revertEntry, CancellationToken ct = default);
}

public sealed record InboxJournalPage(IReadOnlyList<InboxDecisionJournalEntry> Entries, int Total);
