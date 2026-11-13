using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Support;

/// <summary>
/// The tenantId argument is an explicit scope on top of RLS and the tenant query filter: tenant handlers
/// always pass the caller's tenant, admin handlers pass null to reach every tenant.
/// </summary>
public sealed class EfSupportTicketRepository : ISupportTicketRepository
{
    private readonly ApplicationDbContext _db;

    public EfSupportTicketRepository(ApplicationDbContext db) => _db = db;

    public async Task<(IReadOnlyList<SupportTicketRow> Items, int Total)> ListAsync(
        SupportTicketFilter filter, CancellationToken ct = default)
    {
        var query = _db.SupportTickets.AsNoTracking().AsQueryable();

        if (filter.TenantId is { } tenantId) query = query.Where(t => t.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(filter.Status)) query = query.Where(t => t.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.Priority)) query = query.Where(t => t.Priority == filter.Priority);
        if (filter.UnassignedOnly)
        {
            query = query.Where(t => t.AssignedToPlatformUserId == null);
        }
        else if (filter.AssignedToPlatformUserId is { } assignee)
        {
            query = query.Where(t => t.AssignedToPlatformUserId == assignee);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(t => t.Subject.Contains(term));
        }

        if (filter.AwaitingSupportOnly) query = query.Where(t => t.AwaitingSupport);

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(t => new
            {
                Ticket = t,
                TenantName = _db.Tenants.Where(x => x.Id == t.TenantId).Select(x => x.Name).FirstOrDefault(),
                AssigneeName = _db.PlatformUsers
                    .Where(p => p.Id == t.AssignedToPlatformUserId)
                    .Select(p => p.FullName)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new SupportTicketRow(r.Ticket, r.TenantName ?? string.Empty, r.AssigneeName))
            .ToList();
        return (items, total);
    }

    public Task<SupportTicket?> GetWithChildrenAsync(Guid ticketId, Guid? tenantId, CancellationToken ct = default) =>
        Scoped(tenantId)
            .Include(t => t.Comments)
            .Include(t => t.Attachments)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct);

    public Task<SupportTicket?> GetAsync(Guid ticketId, Guid? tenantId, CancellationToken ct = default) =>
        Scoped(tenantId).FirstOrDefaultAsync(t => t.Id == ticketId, ct);

    public Task<SupportTicketAttachment?> GetAttachmentAsync(
        Guid ticketId, Guid attachmentId, Guid? tenantId, CancellationToken ct = default)
    {
        var query = _db.SupportTicketAttachments.AsNoTracking()
            .Where(a => a.Id == attachmentId && a.TicketId == ticketId);
        if (tenantId is { } id) query = query.Where(a => a.TenantId == id);
        return query.FirstOrDefaultAsync(ct);
    }

    public Task<int> CountAttachmentsAsync(Guid ticketId, CancellationToken ct = default) =>
        _db.SupportTicketAttachments.CountAsync(a => a.TicketId == ticketId, ct);

    public async Task<SupportCounts> CountAsync(Guid platformUserId, CancellationToken ct = default)
    {
        var counts = await _db.SupportTickets.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Awaiting = g.Count(t => t.AwaitingSupport),
                AwaitingMine = g.Count(t => t.AwaitingSupport && t.AssignedToPlatformUserId == platformUserId),
                OpenUnassigned = g.Count(t => t.AssignedToPlatformUserId == null
                    && (t.Status == SupportTicket.StatusOpen || t.Status == SupportTicket.StatusInProgress)),
            })
            .FirstOrDefaultAsync(ct);

        return counts is null
            ? new SupportCounts(0, 0, 0)
            : new SupportCounts(counts.Awaiting, counts.AwaitingMine, counts.OpenUnassigned);
    }

    public async Task<IReadOnlyList<Guid>> ListTenantCommenterIdsAsync(Guid ticketId, CancellationToken ct = default) =>
        await _db.SupportTicketComments.AsNoTracking()
            .Where(c => c.TicketId == ticketId && c.AuthorUserId != null)
            .Select(c => c.AuthorUserId!.Value)
            .Distinct()
            .ToListAsync(ct);

    public Task<string?> GetActivePlatformUserEmailAsync(Guid platformUserId, CancellationToken ct = default) =>
        _db.PlatformUsers.AsNoTracking()
            .Where(p => p.Id == platformUserId && p.Status == PlatformUser.StatusActive)
            .Select(p => p.Email)
            .FirstOrDefaultAsync(ct);

    public Task<string?> GetTenantNameAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(ct);

    public Task<string?> GetActivePlatformUserNameAsync(Guid platformUserId, CancellationToken ct = default) =>
        _db.PlatformUsers.AsNoTracking()
            .Where(p => p.Id == platformUserId && p.Status == PlatformUser.StatusActive)
            .Select(p => p.FullName)
            .FirstOrDefaultAsync(ct);

    public Task<string?> GetPlatformUserNameAsync(Guid platformUserId, CancellationToken ct = default) =>
        _db.PlatformUsers.AsNoTracking()
            .Where(p => p.Id == platformUserId)
            .Select(p => p.FullName)
            .FirstOrDefaultAsync(ct);

    public async Task<string?> GetTenantUserNameAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FirstName, u.LastName, u.Email })
            .FirstOrDefaultAsync(ct);
        if (user is null) return null;

        var name = $"{user.FirstName} {user.LastName}".Trim();
        return name.Length > 0 ? name : user.Email;
    }

    public async Task<IReadOnlyList<SupportAssigneeRow>> ListActivePlatformUsersAsync(CancellationToken ct = default) =>
        await _db.PlatformUsers.AsNoTracking()
            .Where(p => p.Status == PlatformUser.StatusActive)
            .OrderBy(p => p.FullName)
            .Select(p => new SupportAssigneeRow(p.Id, p.FullName, p.Email))
            .ToListAsync(ct);

    public async Task AddAsync(SupportTicket ticket, CancellationToken ct = default) =>
        await _db.SupportTickets.AddAsync(ticket, ct);

    public async Task AddCommentAsync(SupportTicketComment comment, CancellationToken ct = default) =>
        await _db.SupportTicketComments.AddAsync(comment, ct);

    public async Task AddAttachmentAsync(SupportTicketAttachment attachment, CancellationToken ct = default) =>
        await _db.SupportTicketAttachments.AddAsync(attachment, ct);

    private IQueryable<SupportTicket> Scoped(Guid? tenantId)
    {
        var query = _db.SupportTickets.AsQueryable();
        return tenantId is { } id ? query.Where(t => t.TenantId == id) : query;
    }
}
