using ONEVO.Domain.Features.DevPlatform.SystemConfig.TrayReleases.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.RepositoryInterfaces;

public interface ITrayAppReleaseRepository
{
    Task<IReadOnlyList<TrayAppRelease>> ListAllAsync(CancellationToken ct);
    /// <summary>Tracked entity (for updates).</summary>
    Task<TrayAppRelease?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<TrayAppRelease?> GetByChannelAndVersionAsync(string channel, string version, CancellationToken ct);
    /// <summary>Highest x.y.z among active rows in the channel; null when none.</summary>
    Task<TrayAppRelease?> GetLatestActiveAsync(string channel, CancellationToken ct);
    /// <summary>Adds only; does not save.</summary>
    Task AddAsync(TrayAppRelease release, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
