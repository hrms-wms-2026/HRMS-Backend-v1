using ONEVO.Domain.Features.DevPlatform.SystemConfig.FeatureFlags.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.FeatureFlags.RepositoryInterfaces;

public interface IPlatformFeatureFlagRepository
{
    Task<IReadOnlyList<PlatformFeatureFlag>> ListAsync(CancellationToken ct = default);
    Task<PlatformFeatureFlag?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsByKeyAsync(string key, CancellationToken ct = default);
    Task AddAsync(PlatformFeatureFlag flag, CancellationToken ct = default);
    void UpdateFlag(PlatformFeatureFlag flag);
}
