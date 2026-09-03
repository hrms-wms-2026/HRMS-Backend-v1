using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories;

public class EfEntityAssetRepository : IEntityAssetRepository
{
    private readonly ApplicationDbContext _db;

    public EfEntityAssetRepository(ApplicationDbContext db) => _db = db;

    public async Task AddAsync(EntityAsset asset, CancellationToken ct = default)
    {
        await _db.EntityAssets.AddAsync(asset, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetPrimaryFileIdsByOwnerAsync(
        Guid tenantId, string ownerType, IReadOnlyCollection<Guid> ownerIds, string assetPurpose, CancellationToken ct = default)
    {
        if (ownerIds.Count == 0)
            return new Dictionary<Guid, Guid>();

        return await _db.EntityAssets.AsNoTracking()
            .Where(a =>
                a.TenantId == tenantId &&
                a.OwnerType == ownerType &&
                a.AssetPurpose == assetPurpose &&
                a.IsPrimary &&
                ownerIds.Contains(a.OwnerId))
            .ToDictionaryAsync(a => a.OwnerId, a => a.FileRecordId, ct);
    }

    public async Task<IReadOnlyList<EntityAssetWithFile>> ListByOwnerAsync(
        Guid tenantId, string ownerType, Guid ownerId, CancellationToken ct = default)
    {
        return await _db.EntityAssets.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.OwnerType == ownerType && a.OwnerId == ownerId)
            .Join(_db.FileRecords.AsNoTracking(), a => a.FileRecordId, f => f.Id,
                (a, f) => new EntityAssetWithFile(a.Id, f.Id, f.OriginalFileName, f.FileSizeBytes, f.ContentType, a.CreatedAt))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<EntityAsset?> GetByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.EntityAssets.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == id, ct);
    }

    public Task DeleteAsync(EntityAsset asset, CancellationToken ct = default)
    {
        _db.EntityAssets.Remove(asset);
        return Task.CompletedTask;
    }
}
