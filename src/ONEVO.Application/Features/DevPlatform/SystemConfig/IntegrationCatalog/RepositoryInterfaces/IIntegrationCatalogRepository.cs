using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.RepositoryInterfaces;

public interface IIntegrationCatalogRepository
{
    Task<IReadOnlyList<IntegrationCatalogEntry>> ListAllAsync(CancellationToken ct);
    Task<IntegrationCatalogEntry?> GetByKeyAsync(string integrationKey, CancellationToken ct);
    Task<IReadOnlyList<ModuleIntegrationLink>> ListAllLinksAsync(CancellationToken ct);
    Task<IReadOnlyList<string>> GetLinkedModuleKeysAsync(string integrationKey, CancellationToken ct);
    Task<ModuleIntegrationLink?> GetLinkAsync(string moduleKey, string integrationKey, CancellationToken ct);
    Task AddAsync(IntegrationCatalogEntry entry, CancellationToken ct);
    Task AddLinkAsync(ModuleIntegrationLink link, CancellationToken ct);
    Task RemoveLinkAsync(ModuleIntegrationLink link, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
