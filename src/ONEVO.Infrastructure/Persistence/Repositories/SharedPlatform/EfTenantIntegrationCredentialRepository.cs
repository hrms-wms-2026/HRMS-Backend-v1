using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;
using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.SharedPlatform;

public sealed class EfTenantIntegrationCredentialRepository : ITenantIntegrationCredentialRepository
{
    private readonly ApplicationDbContext _db;
    public EfTenantIntegrationCredentialRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<bool> TenantExistsAsync(Guid tenantId, CancellationToken ct)
    {
        return _db.Tenants.AsNoTracking().AnyAsync(x => x.Id == tenantId, ct);
    }

    public Task<IntegrationCatalogEntry?> GetIntegrationAsync(string integrationKey, CancellationToken ct)
    {
        return _db.IntegrationCatalogEntries.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IntegrationKey == integrationKey, ct);
    }

    public async Task<IReadOnlyList<TenantIntegrationCredential>> ListByTenantAsync(
        Guid tenantId, CancellationToken ct)
    {
        return await _db.TenantIntegrationCredentials.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.IntegrationKey)
            .ToListAsync(ct);
    }

    public Task<TenantIntegrationCredential?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return _db.TenantIntegrationCredentials.FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public Task<TenantIntegrationCredential?> GetByTenantAndIntegrationAsync(
        Guid tenantId, string integrationKey, CancellationToken ct)
    {
        return _db.TenantIntegrationCredentials.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.IntegrationKey == integrationKey, ct);
    }

    public async Task AddAsync(TenantIntegrationCredential credential, CancellationToken ct)
    {
        await _db.TenantIntegrationCredentials.AddAsync(credential, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        return _db.SaveChangesAsync(ct);
    }
}
