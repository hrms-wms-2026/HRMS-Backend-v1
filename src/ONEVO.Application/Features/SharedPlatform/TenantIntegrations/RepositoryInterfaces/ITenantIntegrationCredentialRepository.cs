using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;
using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;

public interface ITenantIntegrationCredentialRepository
{
    Task<bool> TenantExistsAsync(Guid tenantId, CancellationToken ct);
    Task<IntegrationCatalogEntry?> GetIntegrationAsync(string integrationKey, CancellationToken ct);
    Task<IReadOnlyList<TenantIntegrationCredential>> ListByTenantAsync(Guid tenantId, CancellationToken ct);
    Task<TenantIntegrationCredential?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<TenantIntegrationCredential?> GetByTenantAndIntegrationAsync(Guid tenantId, string integrationKey, CancellationToken ct);
    Task AddAsync(TenantIntegrationCredential credential, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
