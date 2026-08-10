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
}
