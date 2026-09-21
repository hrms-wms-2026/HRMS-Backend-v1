using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.TrayReleases.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig;

public sealed class EfTrayAppReleaseRepository : ITrayAppReleaseRepository
{
    private readonly ApplicationDbContext _db;

    public EfTrayAppReleaseRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<TrayAppRelease>> ListAllAsync(CancellationToken ct)
    {
        var rows = await _db.TrayAppReleases.AsNoTracking().ToListAsync(ct);
        // Version ordering must be numeric; do it in memory (the table is tiny).
        return rows
            .OrderByDescending(r => TrayReleaseVersion.TryParse(r.Version, out var v) ? v : new Version(0, 0, 0))
            .ThenBy(r => r.Channel)
            .ToList();
    }

    public Task<TrayAppRelease?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.TrayAppReleases.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<TrayAppRelease?> GetByChannelAndVersionAsync(string channel, string version, CancellationToken ct) =>
        _db.TrayAppReleases.FirstOrDefaultAsync(r => r.Channel == channel && r.Version == version, ct);

    public async Task<TrayAppRelease?> GetLatestActiveAsync(string channel, CancellationToken ct)
    {
        var active = await _db.TrayAppReleases.AsNoTracking()
            .Where(r => r.Channel == channel && r.IsActive)
            .ToListAsync(ct);

        return active
            .Where(r => TrayReleaseVersion.TryParse(r.Version, out _))
            .OrderByDescending(r => { TrayReleaseVersion.TryParse(r.Version, out var v); return v; })
            .FirstOrDefault();
    }

    public async Task AddAsync(TrayAppRelease release, CancellationToken ct) =>
        await _db.TrayAppReleases.AddAsync(release, ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
