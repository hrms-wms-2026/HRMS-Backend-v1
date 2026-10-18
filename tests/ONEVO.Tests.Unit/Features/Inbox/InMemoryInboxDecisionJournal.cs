using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Domain.Features.Inbox.Entities;

namespace ONEVO.Tests.Unit.Features.Inbox;

/// <summary>List-backed <see cref="IInboxDecisionJournal"/> with the EF implementation's ordering rules.</summary>
public sealed class InMemoryInboxDecisionJournal : IInboxDecisionJournal
{
    public List<InboxDecisionJournalEntry> Entries { get; } = new();

    public Task RecordAsync(InboxDecisionJournalEntry entry, CancellationToken ct = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<InboxDecisionJournalEntry?> LatestAsync(Guid tenantId, ItemRef item, CancellationToken ct = default)
        => Task.FromResult(ForItem(tenantId, item).OrderByDescending(e => e.DecidedAt).ThenByDescending(e => e.CreatedAt).FirstOrDefault());

    public Task<IReadOnlyList<InboxDecisionJournalEntry>> ListForItemAsync(Guid tenantId, ItemRef item, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<InboxDecisionJournalEntry>>(ForItem(tenantId, item).OrderBy(e => e.DecidedAt).ThenBy(e => e.CreatedAt).ToList());

    public Task<InboxJournalPage> ListByActorAsync(
        Guid tenantId, Guid actorEmployeeId, DateTimeOffset? from, DateTimeOffset? to, string? source, string? kind,
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = Entries.Where(e => e.TenantId == tenantId && e.ActorEmployeeId == actorEmployeeId
            && (from == null || e.DecidedAt >= from) && (to == null || e.DecidedAt <= to)
            && (string.IsNullOrEmpty(source) || e.Source == source) && (string.IsNullOrEmpty(kind) || e.Kind == kind)).ToList();
        var rows = query.OrderByDescending(e => e.DecidedAt).ThenByDescending(e => e.CreatedAt)
            .Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(new InboxJournalPage(rows, query.Count));
    }

    public Task<IReadOnlyList<InboxDecisionJournalEntry>> ListLatestEscalationsAsync(
        Guid tenantId, string source, string kind, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<InboxDecisionJournalEntry>>(Entries
            .Where(e => e.TenantId == tenantId && e.Source == source && e.Kind == kind
                        && e.Action == InboxJournalActions.Escalate && e.RevertedByJournalId == null)
            .GroupBy(e => e.ItemId)
            .Select(g => g.OrderByDescending(e => e.DecidedAt).ThenByDescending(e => e.CreatedAt).First())
            .ToList());

    public Task<bool> MarkRevertedAsync(InboxDecisionJournalEntry original, InboxDecisionJournalEntry revertEntry, CancellationToken ct = default)
    {
        if (original.RevertedByJournalId is not null)
            return Task.FromResult(false);
        Entries.Add(revertEntry);
        original.RevertedByJournalId = revertEntry.Id;
        return Task.FromResult(true);
    }

    public InboxDecisionJournalEntry Add(
        Guid tenantId, ItemRef item, string action, Guid actor, DateTimeOffset decidedAt, string? afterJson = null)
    {
        var entry = new InboxDecisionJournalEntry
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Source = item.Source, Kind = item.Kind, ItemId = item.Id, Action = action,
            ActorEmployeeId = actor, DecidedAt = decidedAt, CreatedAt = decidedAt, AfterJson = afterJson,
        };
        Entries.Add(entry);
        return entry;
    }

    private IEnumerable<InboxDecisionJournalEntry> ForItem(Guid tenantId, ItemRef item)
        => Entries.Where(e => e.TenantId == tenantId && e.Source == item.Source && e.Kind == item.Kind && e.ItemId == item.Id);
}
