using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Domain.Features.Inbox.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Inbox;

/// <summary>Task 37: inbox_items and inbox_item_assignees on the request's own DbContext (so staged rows are saved by
/// the module's SaveChanges, in its transaction).</summary>
public sealed class EfInboxEntryStore(ApplicationDbContext db) : IInboxEntryStore
{
    public async Task StageAsync(InboxEntryState state, CancellationToken ct)
    {
        var entry = await db.InboxEntries
            .Include(e => e.Assignees)
            .SingleOrDefaultAsync(e => e.TenantId == state.TenantId && e.Source == state.Source && e.Kind == state.Kind
                && e.SubjectId == state.SubjectId, ct);
        if (entry is null)
        {
            entry = new InboxEntry
            {
                Id = Guid.NewGuid(), TenantId = state.TenantId, Source = state.Source, Kind = state.Kind, SubjectId = state.SubjectId,
            };
            db.InboxEntries.Add(entry);
        }

        entry.Category = state.Category;
        entry.SubjectEmployeeId = state.SubjectEmployeeId;
        entry.RequestedByEmployeeId = state.RequestedByEmployeeId;
        entry.Status = state.Status;
        entry.Title = Truncate(state.Title, 200);
        entry.Summary = state.Summary is null ? null : Truncate(state.Summary, 400);
        entry.CreatedAt = state.CreatedAt;
        entry.UpdatedAt = state.UpdatedAt;
        entry.DecidedAt = state.DecidedAt;
        entry.DecidedByEmployeeId = state.DecidedByEmployeeId;

        var wanted = state.Assignees.ToDictionary(a => a.EmployeeId);
        foreach (var stale in entry.Assignees.Where(a => !wanted.ContainsKey(a.EmployeeId)).ToList())
        {
            entry.Assignees.Remove(stale);
            db.InboxEntryAssignees.Remove(stale);
        }
        foreach (var assignee in state.Assignees)
        {
            var row = entry.Assignees.SingleOrDefault(a => a.EmployeeId == assignee.EmployeeId);
            if (row is null)
            {
                row = new InboxEntryAssignee
                {
                    Id = Guid.NewGuid(), TenantId = state.TenantId, InboxEntryId = entry.Id, EmployeeId = assignee.EmployeeId,
                };
                entry.Assignees.Add(row);
            }
            row.Source = state.Source;
            row.Kind = state.Kind;
            row.Status = assignee.Status;
            row.SequenceOrder = assignee.SequenceOrder;
            row.UpdatedAt = state.UpdatedAt;
        }
    }

    public async Task<InboxEntryState?> GetAsync(Guid tenantId, string source, string kind, Guid subjectId, CancellationToken ct)
    {
        var entry = await db.InboxEntries.AsNoTracking()
            .Include(e => e.Assignees)
            .SingleOrDefaultAsync(e => e.TenantId == tenantId && e.Source == source && e.Kind == kind && e.SubjectId == subjectId, ct);
        return entry is null
            ? null
            : new InboxEntryState(
                entry.TenantId, entry.Source, entry.Kind, entry.SubjectId, entry.Category, entry.SubjectEmployeeId,
                entry.RequestedByEmployeeId, entry.Status, entry.Title, entry.Summary, entry.CreatedAt, entry.UpdatedAt,
                entry.DecidedAt, entry.DecidedByEmployeeId,
                entry.Assignees.OrderBy(a => a.SequenceOrder).ThenBy(a => a.EmployeeId)
                    .Select(a => new InboxEntryAssigneeState(a.EmployeeId, a.Status, a.SequenceOrder)).ToList());
    }

    public async Task<IReadOnlyDictionary<(string Source, string Kind), int>> CountPendingForAsync(
        Guid tenantId, Guid employeeId, IReadOnlyCollection<string> sources, CancellationToken ct)
    {
        if (sources.Count == 0)
            return new Dictionary<(string, string), int>();
        var rows = await db.InboxEntryAssignees.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.EmployeeId == employeeId && a.Status == InboxEntryAssigneeStatuses.Pending
                && sources.Contains(a.Source))
            .GroupBy(a => new { a.Source, a.Kind })
            .Select(g => new { g.Key.Source, g.Key.Kind, Count = g.Count() })
            .ToListAsync(ct);
        return rows.ToDictionary(r => (r.Source, r.Kind), r => r.Count);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
