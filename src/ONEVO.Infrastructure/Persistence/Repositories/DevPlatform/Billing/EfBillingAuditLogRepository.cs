using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Billing;

public sealed class EfBillingAuditLogRepository : IBillingAuditLogRepository
{
    private readonly ApplicationDbContext _db;

    public EfBillingAuditLogRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(BillingAuditLog auditLog, CancellationToken ct = default) =>
        await _db.BillingAuditLogs.AddAsync(auditLog, ct);

    public async Task<IReadOnlyList<BillingAuditLog>> ListByInvoiceAsync(
        Guid invoiceId,
        CancellationToken ct = default) =>
        await _db.BillingAuditLogs
            .AsNoTracking()
            .Where(l => l.InvoiceId == invoiceId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<BillingAuditLog>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default) =>
        await _db.BillingAuditLogs
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);
}
