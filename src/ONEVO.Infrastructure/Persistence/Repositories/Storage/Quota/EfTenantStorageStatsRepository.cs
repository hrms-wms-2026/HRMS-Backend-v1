using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Storage.Quota.RepositoryInterfaces;
using ONEVO.Domain.Features.Storage.Quota.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Storage.Quota;

public sealed class EfTenantStorageStatsRepository : ITenantStorageStatsRepository
{
    private readonly ApplicationDbContext _db;

    public EfTenantStorageStatsRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    // The explicit tenant_id predicate is intentional even though the global
    // tenant query filter also applies in tenant mode: quota checks must stay
    // tenant-scoped in System/Admin context modes too.
    public async Task<TenantStorageStats?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        var query = _db.TenantStorageStats
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId);

        return await query.FirstOrDefaultAsync(ct);
    }

    public async Task<bool> TryReserveBytesAsync(Guid tenantId, long bytes, long limitBytes, CancellationToken ct = default)
    {
        // A brand-new tenant has no tenant_storage_stats row yet. Guard the
        // pure-insert path here; the WHERE clause below guards the
        // already-has-usage path taken by the MERGE update branch.
        if (bytes > limitBytes)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;

        var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            MERGE tenant_storage_stats WITH (HOLDLOCK) AS target
            USING (SELECT {tenantId} AS tenant_id) AS source
                ON target.tenant_id = source.tenant_id
            WHEN MATCHED AND (target.used_r2_bytes + target.reserved_r2_bytes + {bytes}) <= {limitBytes} THEN
                UPDATE SET reserved_r2_bytes = target.reserved_r2_bytes + {bytes}, updated_at = {now}
            WHEN NOT MATCHED THEN
                INSERT (tenant_id, used_r2_bytes, used_db_bytes, reserved_r2_bytes, last_calculated_at, updated_at)
                VALUES ({tenantId}, 0, 0, {bytes}, {now}, {now});
        ", ct);

        return rowsAffected > 0;
    }

    public async Task ReleaseReservedBytesAsync(Guid tenantId, long bytes, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE tenant_storage_stats
            SET reserved_r2_bytes = CASE WHEN reserved_r2_bytes > {bytes} THEN reserved_r2_bytes - {bytes} ELSE 0 END,
                updated_at = {now}
            WHERE tenant_id = {tenantId}
        ", ct);
    }

    public async Task CommitReservedToUsedAsync(Guid tenantId, long bytes, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE tenant_storage_stats
            SET reserved_r2_bytes = CASE WHEN reserved_r2_bytes > {bytes} THEN reserved_r2_bytes - {bytes} ELSE 0 END,
                used_r2_bytes = used_r2_bytes + {bytes},
                updated_at = {now}
            WHERE tenant_id = {tenantId}
        ", ct);
    }

    public async Task ReleaseUsedBytesAsync(Guid tenantId, long bytes, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE tenant_storage_stats
            SET used_r2_bytes = CASE WHEN used_r2_bytes > {bytes} THEN used_r2_bytes - {bytes} ELSE 0 END,
                updated_at = {now}
            WHERE tenant_id = {tenantId}
        ", ct);
    }
}
