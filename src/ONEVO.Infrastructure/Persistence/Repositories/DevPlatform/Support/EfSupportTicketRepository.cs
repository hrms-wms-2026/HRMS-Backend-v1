using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Support;

public sealed class EfSupportTicketRepository : ISupportTicketRepository
{
    private readonly ApplicationDbContext _db;

    public EfSupportTicketRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<SupportTicket?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var ticket = await _db.SupportTickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        return ticket;
    }

    public async Task<SupportTicket?> GetByIdWithCommentsAsync(Guid id, CancellationToken ct = default)
    {
        var ticket = await _db.SupportTickets
            .Include(t => t.Comments)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        return ticket;
    }

    public async Task<IReadOnlyList<SupportTicket>> ListAsync(
        string? status,
        string? priority,
        Guid? tenantId,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        var query = BuildFilteredQuery(status, priority, tenantId);
        var items = await query.OrderByDescending(t => t.CreatedAt).Skip(skip).Take(take).ToListAsync(ct);
        return items;
    }

    public async Task<int> CountAsync(
        string? status,
        string? priority,
        Guid? tenantId,
        CancellationToken ct = default)
    {
        var query = BuildFilteredQuery(status, priority, tenantId);
        var count = await query.CountAsync(ct);
        return count;
    }

    public async Task AddAsync(SupportTicket ticket, CancellationToken ct = default)
    {
        await _db.SupportTickets.AddAsync(ticket, ct);
    }

    public async Task AddCommentAsync(SupportTicketComment comment, CancellationToken ct = default)
    {
        await _db.SupportTicketComments.AddAsync(comment, ct);
    }

    private IQueryable<SupportTicket> BuildFilteredQuery(string? status, string? priority, Guid? tenantId)
    {
        var query = _db.SupportTickets.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(t => t.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(priority))
        {
            query = query.Where(t => t.Priority == priority);
        }

        if (tenantId.HasValue)
        {
            query = query.Where(t => t.TenantId == tenantId.Value);
        }

        return query;
    }
}
