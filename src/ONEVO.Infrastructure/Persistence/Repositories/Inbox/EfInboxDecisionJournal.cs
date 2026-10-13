using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Domain.Features.Inbox.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Inbox;

public class EfInboxDecisionJournal : IInboxDecisionJournal
{
    private readonly ApplicationDbContext _db;

    public EfInboxDecisionJournal(ApplicationDbContext db) => _db = db;

    public async Task RecordAsync(InboxDecisionJournalEntry entry, CancellationToken ct = default)
    {
        await _db.InboxDecisionJournal.AddAsync(entry, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<InboxDecisionJournalEntry?> LatestAsync(Guid tenantId, ItemRef item, CancellationToken ct = default)
        => await ForItem(tenantId, item).OrderByDescending(e => e.DecidedAt).ThenByDescending(e => e.CreatedAt).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<InboxDecisionJournalEntry>> ListForItemAsync(Guid tenantId, ItemRef item, CancellationToken ct = default)
        => await ForItem(tenantId, item).AsNoTracking().OrderBy(e => e.DecidedAt).ThenBy(e => e.CreatedAt).ToListAsync(ct);

    public async Task<InboxJournalPage> ListByActorAsync(
        Guid tenantId, Guid actorEmployeeId, DateTimeOffset? from, DateTimeOffset? to, string? source, string? kind,
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.InboxDecisionJournal.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.ActorEmployeeId == actorEmployeeId);
        if (from is { } since)
            query = query.Where(e => e.DecidedAt >= since);
        if (to is { } until)
            query = query.Where(e => e.DecidedAt <= until);
        if (!string.IsNullOrEmpty(source))
            query = query.Where(e => e.Source == source);
        if (!string.IsNullOrEmpty(kind))
            query = query.Where(e => e.Kind == kind);

        var total = await query.CountAsync(ct);
        var entries = await query.OrderByDescending(e => e.DecidedAt).ThenByDescending(e => e.CreatedAt)
            .Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new InboxJournalPage(entries, total);
    }

    public async Task MarkRevertedAsync(InboxDecisionJournalEntry original, InboxDecisionJournalEntry revertEntry, CancellationToken ct = default)
    {
        await _db.InboxDecisionJournal.AddAsync(revertEntry, ct);
        original.RevertedByJournalId = revertEntry.Id;
        if (_db.Entry(original).State == EntityState.Detached)
            _db.InboxDecisionJournal.Update(original);
        await _db.SaveChangesAsync(ct);
    }

    private IQueryable<InboxDecisionJournalEntry> ForItem(Guid tenantId, ItemRef item)
        => _db.InboxDecisionJournal.Where(e => e.TenantId == tenantId && e.Source == item.Source && e.Kind == item.Kind && e.ItemId == item.Id);
}
