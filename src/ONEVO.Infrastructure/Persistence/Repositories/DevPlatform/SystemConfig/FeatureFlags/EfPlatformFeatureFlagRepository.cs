using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.FeatureFlags.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig.FeatureFlags;

public sealed class EfPlatformFeatureFlagRepository : IPlatformFeatureFlagRepository
{
    private readonly ApplicationDbContext _db;

    public EfPlatformFeatureFlagRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<PlatformFeatureFlag>> ListAsync(CancellationToken ct = default) =>
        await _db.PlatformFeatureFlags.AsNoTracking().ToListAsync(ct);

    public Task<PlatformFeatureFlag?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.PlatformFeatureFlags.FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<bool> ExistsByKeyAsync(string key, CancellationToken ct = default) =>
        _db.PlatformFeatureFlags.AnyAsync(f => f.Key == key, ct);

    public async Task AddAsync(PlatformFeatureFlag flag, CancellationToken ct = default) =>
        await _db.PlatformFeatureFlags.AddAsync(flag, ct);

    public void UpdateFlag(PlatformFeatureFlag flag) => _db.PlatformFeatureFlags.Update(flag);
}
