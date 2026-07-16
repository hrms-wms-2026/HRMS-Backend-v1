using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig;

public sealed class EfIntegrationCatalogRepository : IIntegrationCatalogRepository
{
    private readonly ApplicationDbContext _db;
    public EfIntegrationCatalogRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<IntegrationCatalogEntry>> ListAllAsync(CancellationToken ct)
    {
        return await _db.IntegrationCatalogEntries
            .AsNoTracking()
            .OrderBy(entry => entry.IntegrationKey)
            .ToListAsync(ct);
    }

    public Task<IntegrationCatalogEntry?> GetByKeyAsync(string key, CancellationToken ct)
    {
        return _db.IntegrationCatalogEntries
            .FirstOrDefaultAsync(entry => entry.IntegrationKey == key, ct);
    }

    public async Task<IReadOnlyList<ModuleIntegrationLink>> ListAllLinksAsync(CancellationToken ct)
    {
        return await _db.ModuleIntegrationLinks
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetLinkedModuleKeysAsync(string key, CancellationToken ct)
    {
        return await _db.ModuleIntegrationLinks
            .AsNoTracking()
            .Where(link => link.IntegrationKey == key)
            .OrderBy(link => link.ModuleKey)
            .Select(link => link.ModuleKey)
            .ToListAsync(ct);
    }

    public Task<ModuleIntegrationLink?> GetLinkAsync(
        string moduleKey,
        string integrationKey,
        CancellationToken ct)
    {
        return _db.ModuleIntegrationLinks.FirstOrDefaultAsync(
            link => link.ModuleKey == moduleKey && link.IntegrationKey == integrationKey,
            ct);
    }
    public async Task AddAsync(IntegrationCatalogEntry entry, CancellationToken ct)
    {
        await _db.IntegrationCatalogEntries.AddAsync(entry, ct);
    }

    public async Task AddLinkAsync(ModuleIntegrationLink link, CancellationToken ct)
    {
        await _db.ModuleIntegrationLinks.AddAsync(link, ct);
    }

    public Task RemoveLinkAsync(ModuleIntegrationLink link, CancellationToken ct)
    {
        _db.ModuleIntegrationLinks.Remove(link);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        return _db.SaveChangesAsync(ct);
    }
}
