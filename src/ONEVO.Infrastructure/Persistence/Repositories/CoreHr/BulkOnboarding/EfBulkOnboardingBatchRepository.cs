using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.BulkOnboarding.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Repositories.CoreHr.BulkOnboarding;

public class EfBulkOnboardingBatchRepository : IBulkOnboardingBatchRepository
{
    private readonly ApplicationDbContext _db;
    public EfBulkOnboardingBatchRepository(ApplicationDbContext db) => _db = db;

    public Task<BulkOnboardingBatch?> GetTrackedAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        _db.Set<BulkOnboardingBatch>().FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == id, ct);

    public Task<BulkOnboardingBatch?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        _db.Set<BulkOnboardingBatch>().AsNoTracking().FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == id, ct);

    public async Task<IReadOnlyList<BulkOnboardingBatchRow>> ListRowsAsync(Guid tenantId, Guid batchId, CancellationToken ct = default) =>
        await _db.Set<BulkOnboardingBatchRow>().AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.BatchId == batchId)
            .OrderBy(r => r.RowNumber).ToListAsync(ct);

    public async Task<IReadOnlyList<BulkOnboardingBatchRow>> ListTrackedRowsAsync(Guid tenantId, Guid batchId, CancellationToken ct = default) =>
        await _db.Set<BulkOnboardingBatchRow>()
            .Where(r => r.TenantId == tenantId && r.BatchId == batchId)
            .OrderBy(r => r.RowNumber).ToListAsync(ct);

    // IgnoreQueryFilters() is defensive here, not strictly load-bearing: EF's own
    // ITenantOwnedEntity filter is already inactive outside TenantContextMode.Tenant. What
    // actually gates this cross-tenant scan is PostgreSQL RLS - the caller (worker, Task 12)
    // must be in admin mode for the mode-aware policy on this table (Task 2) to allow it.
    public Task<BulkOnboardingBatch?> GetOldestPendingAsync(string status, CancellationToken ct = default) =>
        _db.Set<BulkOnboardingBatch>()
            .IgnoreQueryFilters()
            .Where(b => b.Status == status)
            .OrderBy(b => b.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(BulkOnboardingBatch batch, IReadOnlyList<BulkOnboardingBatchRow> rows, CancellationToken ct = default)
    {
        await _db.Set<BulkOnboardingBatch>().AddAsync(batch, ct);
        await _db.Set<BulkOnboardingBatchRow>().AddRangeAsync(rows, ct);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
