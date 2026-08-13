using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Billing;

public sealed class EfSubscriptionInvoiceRepository : ISubscriptionInvoiceRepository
{
    private readonly ApplicationDbContext _db;

    public EfSubscriptionInvoiceRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(SubscriptionInvoice invoice, CancellationToken ct = default) =>
        await _db.SubscriptionInvoices.AddAsync(invoice, ct);

    public Task UpdateAsync(SubscriptionInvoice invoice, CancellationToken ct = default)
    {
        _db.SubscriptionInvoices.Update(invoice);
        return Task.CompletedTask;
    }

    public Task<SubscriptionInvoice?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.SubscriptionInvoices.FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<SubscriptionInvoice?> GetByInvoiceNumberAsync(string invoiceNumber, CancellationToken ct = default) =>
        _db.SubscriptionInvoices.FirstOrDefaultAsync(i => i.InvoiceNumber == invoiceNumber, ct);

    public Task<SubscriptionInvoice?> GetByExternalInvoiceIdAsync(string externalInvoiceId, CancellationToken ct = default) =>
        _db.SubscriptionInvoices.FirstOrDefaultAsync(i => i.ExternalInvoiceId == externalInvoiceId, ct);

    public async Task<IReadOnlyList<SubscriptionInvoice>> ListAsync(
        SubscriptionInvoiceListFilter filter,
        CancellationToken ct = default)
    {
        var query = _db.SubscriptionInvoices.AsNoTracking();

        if (filter.TenantId.HasValue)
            query = query.Where(i => i.TenantId == filter.TenantId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(i => i.Status == filter.Status);

        if (filter.From.HasValue)
            query = query.Where(i => i.CreatedAt >= filter.From.Value);

        if (filter.To.HasValue)
            query = query.Where(i => i.CreatedAt <= filter.To.Value);

        return await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SubscriptionInvoice>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default) =>
        await _db.SubscriptionInvoices
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);
}
