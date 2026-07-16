using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.ModuleCatalog.RepositoryInterfaces;

public interface IModuleCatalogRepository
{
    Task<IReadOnlyList<ModuleCatalogItem>> GetAllAsync(CancellationToken ct = default);
    Task<ModuleCatalogItem?> GetByKeyAsync(string moduleKey, CancellationToken ct = default);
}
